using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Models;
using Microsoft.Extensions.Logging;
using Polly;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Channels.SendChatMessage;
using TwitchLib.Client.Events;
using TwitchLib.Communication.Interfaces;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Core.EventArgs.User;
using TwitchLib.EventSub.Websockets;

namespace ButterBror.ChatModules.Twitch.Services;

public class TwitchBotClient : ITwitchWhisperClient, IDisposable
{
    private ITwitchClient _ircClient;
    private TwitchAPI _clientAPI = null!;
    private EventSubWebsocketClient _client = null!;
    private readonly ILogger<TwitchLibClient> _logger;

    public readonly List<string> Channels;
    private readonly HashSet<string> _сonnectedChannels = new();
    private readonly ConcurrentDictionary<string, string> _channelIdCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly ResiliencePipeline _twitchPipeline;
    private readonly ResiliencePipeline _apiPipeline;

    public event EventHandler<Events.OnMessageReceivedArgs>? OnMessageReceived;
    public event EventHandler<OnConnectedEventArgs>? OnConnected;
    public event EventHandler<OnDisconnectedArgs>? OnDisconnected;
    public event EventHandler<Events.OnUserJoinedArgs>? OnUserJoined;
    public event EventHandler<Events.OnUserLeftArgs>? OnUserLeft;
    public event EventHandler<Events.OnNewSubscriberArgs>? OnNewSubscriber;
    public event EventHandler<Events.OnGiftedSubscriptionArgs>? OnGiftedSubscription;
    public event EventHandler<Events.OnRaidNotificationArgs>? OnRaidNotification;
    public event EventHandler<OnBitsReceivedArgs>? OnBitsReceived;
    public event EventHandler<UserWhisperMessageArgs>? OnWhisper;

    public bool _connected = false;
    private string _botId = string.Empty;

    public bool IsConnected => _connected;
    public HashSet<string> ConnectedChannels => _сonnectedChannels;

    public TwitchBotClient(
        IEnumerable<string> channels,
        ILogger<TwitchLibClient> logger,
        ResiliencePipeline twitchPipeline,
        ResiliencePipeline apiPipeline,
        ITwitchClient ircClient)
    {
        _logger = logger;
        _ircClient = ircClient;
        _client = new EventSubWebsocketClient();
        _clientAPI = new TwitchAPI();

        Channels = channels?.ToList() ?? new List<string>();
        SetupSubscribes();
    }

    private void SetupSubscribes()
    {
        _client.WebsocketConnected += OnEventSubConnected;
        _client.WebsocketDisconnected += OnEventSubDisconnected;
        _client.WebsocketReconnected += OnEventSubReconnected;
        _client.ErrorOccurred += OnEventSubError;
        _client.ChannelChatMessage += OnEventSubChatMessage;
        _client.ChannelRaid += OnEventSubRaid;
        _client.ChannelSubscribe += OnEventSubSubscribe;
        _client.ChannelSubscriptionGift += OnEventSubSubscriptionGift;
        _client.ChannelCheer += OnEventSubCheer;
        _client.UserWhisperMessage += OnEventSubWhisper;
    }

    public async Task ConnectAsync(string clientId, string oauthToken, string username)
    {
        try
        {
            _clientAPI.Settings.ClientId = clientId;
            _clientAPI.Settings.AccessToken = oauthToken[6..]; // Removing "oauth:" in start

            _botId = await _twitchPipeline.ExecuteAsync(async ct =>
                (await _clientAPI.Helix.Users.GetUsersAsync(logins: [username])).Users[0].Id
            ) ?? "FAIL";
            _logger.LogInformation("[TWBOT] [API] Initialized. botId={Id}, name={Name}", _botId, username);

            await _client.ConnectAsync();
            foreach (var channelName in Channels)
            {
                await JoinChannelAsync(channelName);
            }
            _logger.LogInformation("[TWBOT] [EventSub] Initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWBOT] [EventSub] Failed to connect");
            throw;
        }
    }

    public async Task JoinChannelAsync(string channel)
    {
        var normalizedChannel = channel.ToLowerInvariant();

        // Skip if already connected
        if (_сonnectedChannels.Contains(normalizedChannel))
        {
            _logger.LogDebug("[TWBOT] [EventSub] Channel #{Channel} already connected", channel);
            return;
        }

        // Try EventSub first
        try
        {
            await ConnectEventSubChannelAsync(channel);
            _сonnectedChannels.Add(normalizedChannel);
            _logger.LogInformation("[TWBOT] [EventSub] Connected to #{Channel}", channel);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TWBOT] [EventSub] Failed to connect to #{Channel}, falling back to Irc", channel);
        }

        // Fallback to Irc
        try
        {
            await _ircClient.JoinChannelAsync(channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWBOT] Failed to connect to #{Channel} via both EventSub and Irc", channel);
            throw;
        }
    }

    private async Task ConnectEventSubChannelAsync(string channel)
    {
        if (!_connected)
        {
            throw new InvalidOperationException("EventSub client is not connected");
        }

        // Get channel ID
        var channelUser = await _twitchPipeline.ExecuteAsync(async ct =>
            (await _clientAPI.Helix.Users.GetUsersAsync(logins: [channel])).Users.FirstOrDefault()
        );

        if (channelUser == null)
        {
            throw new InvalidOperationException($"Channel {channel} not found");
        }

        var channelId = channelUser.Id;

        // Subscribe to chat messages
        await _apiPipeline.ExecuteAsync(async ct =>
            await _clientAPI.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.chat.message", "1",
                new Dictionary<string, string>
                {
                    { "broadcaster_user_id", channelId },
                    { "user_id", _botId }
                },
                TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                _client.SessionId
            )
        );

        // Subscribe to raids
        await _apiPipeline.ExecuteAsync(async ct =>
            await _clientAPI.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.raid", "1",
                new Dictionary<string, string> { { "to_broadcaster_user_id", channelId } },
                TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                _client.SessionId
            )
        );

        // Subscribe to subscriptions
        await _apiPipeline.ExecuteAsync(async ct =>
            await _clientAPI.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.subscribe", "1",
                new Dictionary<string, string> { { "broadcaster_user_id", channelId } },
                TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                _client.SessionId
            )
        );

        // Subscribe to gift subscriptions
        await _apiPipeline.ExecuteAsync(async ct =>
            await _clientAPI.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.subscription.gift", "1",
                new Dictionary<string, string> { { "broadcaster_user_id", channelId } },
                TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                _client.SessionId
            )
        );

        // Subscribe to cheers
        await _apiPipeline.ExecuteAsync(async ct =>
            await _clientAPI.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.cheer", "1",
                new Dictionary<string, string> { { "broadcaster_user_id", channelId } },
                TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                _client.SessionId
            )
        );

        _logger.LogDebug("[TWBOT] Subscribed to EventSub events for #{Channel}", channel);
    }

    public async Task SendWhisperAsync(string recipientUserId, string message)
    {
        if (!_ircClient.IsConnected)
            throw new InvalidOperationException("Not connected to Twitch");

        try
        {
            var sanitizedMessage = SanitizeMessage(message);

            await _twitchPipeline.ExecuteAsync(async ct =>
                await _clientAPI.Helix.Whispers.SendWhisperAsync(_botId, recipientUserId, message, true)
            );
            _logger.LogDebug("[TWBOT] Sent whisper to {Recipient}: \"{Message}\"", recipientUserId, sanitizedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWBOT] Failed to send whisper to {Recipient}", recipientUserId);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            if (_client != null)
            {
                await _client.DisconnectAsync();
            }

            _сonnectedChannels.Clear();
            _connected = false;
            _logger.LogInformation("[TWBOT] Bye bye");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWBOT] Error during disconnection");
            throw;
        }
    }

    public bool IsJoined(string channel)
    {
        return _сonnectedChannels.Contains(channel.ToLowerInvariant());
    }

    public async Task LeaveChannelAsync(string channel)
    {
        var normalizedChannel = channel.ToLowerInvariant();

        if (!_сonnectedChannels.Contains(normalizedChannel))
        {
            _logger.LogDebug("[TWBOT] Not connected to #{Channel}, skip leaving", channel);
            return;
        }

        try
        {
            // Отписка от EventSub (в Websockets Twitch автоматически удаляет подписки при закрытии сессии, 
            // но если нужно удалить конкретный канал во время работы — используется удаление подписок через API)
            // Примечание: Для полноценного удаления подписок "на лету" обычно требуется хранить SubscriptionId, 
            // выданные Twitch при вызове CreateEventSubSubscriptionAsync. 
            // Если это не критично, достаточно просто удалить из локального хэшсета:

            _сonnectedChannels.Remove(normalizedChannel);
            _logger.LogInformation("[TWBOT] Left channel #{Channel}", channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWBOT] Error leaving channel #{Channel}", channel);
            throw;
        }
    }

    public async Task SendMessageAsync(string channel, string message)
    {
        try
        {
            var channelId = await GetChannelIdAsync(channel);
            var sanitizedMessage = SanitizeMessage(message);
            SendChatMessageRequest request = new SendChatMessageRequest()
            {
                BroadcasterId = channelId,
                SenderId = _botId,
                Message = sanitizedMessage
            };

            await _apiPipeline.ExecuteAsync(async ct =>
                await _clientAPI.Helix.Chat.SendChatMessage(request)
            );

            _logger.LogDebug("[TWBOT] Sent message to #{Channel}: \"{Message}\"", channel, sanitizedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWBOT] Failed to send message to #{Channel}", channel);
            throw;
        }
    }

    public async Task SendReplyAsync(string channel, string replyToMessageId, string message)
    {
        try
        {
            var channelId = await GetChannelIdAsync(channel);
            var sanitizedMessage = SanitizeMessage(message);
            SendChatMessageRequest request = new SendChatMessageRequest()
            {
                BroadcasterId = channelId,
                SenderId = _botId,
                Message = sanitizedMessage,
                ReplyParentMessageId = replyToMessageId
            };

            await _apiPipeline.ExecuteAsync(async ct =>
                await _clientAPI.Helix.Chat.SendChatMessage(request)
            );

            _logger.LogDebug("[TWBOT] Sent reply to #{Channel}: \"{Message}\"", channel, sanitizedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWBOT] Failed to send reply to #{Channel}", channel);
            throw;
        }
    }

    private async Task<string> GetChannelIdAsync(string channelName)
    {
        var normalized = channelName.ToLowerInvariant();
        if (_channelIdCache.TryGetValue(normalized, out var cachedId))
        {
            return cachedId;
        }

        var channelUser = await _twitchPipeline.ExecuteAsync(async ct =>
            (await _clientAPI.Helix.Users.GetUsersAsync(logins: [normalized])).Users.FirstOrDefault()
        );

        if (channelUser == null)
        {
            throw new InvalidOperationException($"Channel {normalized} not found");
        }

        _channelIdCache[normalized] = channelUser.Id;
        return channelUser.Id;
    }

    private string SanitizeMessage(string message)
    {
        return Regex.Replace(message, @"\s+", " ")
            .Replace("\r", "")
            .Replace("\n", " ")
            .Trim();
    }

    #region EventSub events
    private async Task OnEventSubReconnected(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketReconnectedArgs e)
    {
        _connected = true;
        _logger.LogInformation("[TWBOT] [EventSub] Reconnected!");
    }

    private async Task OnEventSubDisconnected(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketDisconnectedArgs e)
    {
        _connected = false;
        _logger.LogWarning("[TWBOT] [EventSub] Disconnected");
    }

    private async Task OnEventSubConnected(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketConnectedArgs e)
    {
        await _apiPipeline.ExecuteAsync(async ct =>
                await _clientAPI.Helix.EventSub.CreateEventSubSubscriptionAsync(
                    "user.whisper.message", "1",
                    new Dictionary<string, string> { { "user_id", _botId } },
                    TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                    _client.SessionId
                )
            );

        _connected = true;
        _logger.LogInformation("[TWBOT] [EventSub] Connected!");
    }

    private async Task OnEventSubError(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.ErrorOccuredArgs e)
    {
        _logger.LogError("[TWBOT] EventSub error: {Error}", e.Exception?.Message);
    }

    private async Task OnEventSubChatMessage(object? sender, ChannelChatMessageArgs e)
    {
        try
        {
            var msg = e.Payload.Event;
            var badges = msg.Badges?.Select(b => new KeyValuePair<string, string>(b.SetId, b.Id)).ToList() 
                ?? new List<KeyValuePair<string, string>>();

            var chatMessage = new Models.ChatMessage
            {
                Username = msg.ChatterUserName,
                UserId = msg.ChatterUserId,
                Message = msg.Message.Text,
                Channel = msg.BroadcasterUserLogin,
                ChannelId = msg.BroadcasterUserId,
                IsModerator = msg.IsModerator,
                IsBroadcaster = msg.ChatterUserId == msg.BroadcasterUserId,
                IsSubscriber = msg.IsSubscriber,
                IsVip = msg.IsVip,
                Badges = badges,
                Color = msg.Color,
                MessageId = msg.MessageId
            };

            OnMessageReceived?.Invoke(this, new Events.OnMessageReceivedArgs { ChatMessage = chatMessage });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWBOT] Error handling EventSub chat message");
        }
    }

    private async Task OnEventSubRaid(object? sender, ChannelRaidArgs e)
    {
        try
        {
            var raid = e.Payload.Event;
            var args = new Events.OnRaidNotificationArgs
            {
                Channel = raid.ToBroadcasterUserLogin,
                RaiderUsername = raid.FromBroadcasterUserName,
                ViewerCount = raid.Viewers
            };
            OnRaidNotification?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWBOT] Error handling EventSub raid");
        }
    }

    private async Task OnEventSubSubscribe(object? sender, ChannelSubscribeArgs e)
    {
        try
        {
            var sub = e.Payload.Event;
            var args = new Events.OnNewSubscriberArgs
            {
                Channel = sub.BroadcasterUserLogin,
                Username = sub.UserName,
                SubscriptionPlan = sub.Tier,
                Months = 0
            };
            OnNewSubscriber?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWBOT] Error handling EventSub subscription");
        }
    }

    private async Task OnEventSubSubscriptionGift(object? sender, ChannelSubscriptionGiftArgs e)
    {
        try
        {
            var gift = e.Payload.Event;
            var args = new Events.OnGiftedSubscriptionArgs
            {
                Channel = gift.BroadcasterUserLogin,
                GifterUsername = gift.UserName ?? "Anonymous",
                RecipientUsername = string.Empty,
                SubscriptionPlan = gift.Tier
            };
            OnGiftedSubscription?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWBOT] Error handling EventSub subscription gift");
        }
    }

    private async Task OnEventSubCheer(object? sender, ChannelCheerArgs e)
    {
        try
        {
            var cheer = e.Payload.Event;
            var args = new OnBitsReceivedArgs
            {
                Channel = cheer.BroadcasterUserLogin,
                Username = cheer.UserName,
                Bits = cheer.Bits,
                Message = cheer.Message
            };
            OnBitsReceived?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWBOT] Error handling EventSub cheer");
        }
    }

    private async Task OnEventSubWhisper(object? sender, UserWhisperMessageArgs e)
    {
        try
        {
            OnWhisper?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWBOT] Error handling EventSub whisper");
        }
    }

    #endregion EventSub events

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}
