using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Interfaces;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Data.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix.Models.Channels.SendChatMessage;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Websockets;

namespace ButterBror.ChatModules.Twitch.Services.ChatTransports;

public sealed class EventSubChatTransport : ITwitchChatTransport
{
    private readonly TwitchAPI _api;
    private readonly EventSubWebsocketClient _client;
    private readonly ITwitchTokenManager _tokens;
    private readonly ICustomDataRepository _repository;
    private readonly ILogger<EventSubChatTransport> _logger;
    private readonly TwitchConfiguration _configuration;
    
    private readonly HashSet<string> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    
    private string _botUserId = string.Empty;

    public EventSubChatTransport(
        TwitchAPI api,
        EventSubWebsocketClient client,
        ITwitchTokenManager tokens,
        ICustomDataRepository repository,
        IOptions<TwitchConfiguration> options,
        ILogger<EventSubChatTransport> logger)
    {
        _api = api;
        _client = client;
        _tokens = tokens;
        _repository = repository;
        _logger = logger;
        _configuration = options.Value;
        
        client.ChannelChatMessage += OnChatMessage;
        client.UserWhisperMessage += OnWhisperMessage;
        client.WebsocketConnected += OnWebsocketConnected;
        client.WebsocketReconnected += OnWebsocketReconnected;
        client.WebsocketDisconnected += OnWebsocketDisconnected;
    }

    public string Name => "eventsub";
    public bool IsConnected { get; private set; }
    public IReadOnlyCollection<string> ConnectedChannels => _subscriptions.ToArray();
    public event EventHandler<OnMessageReceivedArgs>? MessageReceived;
    public event EventHandler<OnUserStateChangedArgs>? UserStateChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _botUserId = _configuration.BotUserId;
        _api.Settings.ClientId = _configuration.ClientId;
        _api.Settings.AccessToken = await _tokens.GetUserAccessTokenAsync(cancellationToken).ConfigureAwait(false);
    }
    
    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
            return;

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
                return;

            _logger.LogInformation("[tw:es] connecting eventsub websocket");
            await _client.ConnectAsync(new Uri("wss://eventsub.wss.twitch.tv/ws")).ConfigureAwait(false);
            IsConnected = true;
            _logger.LogInformation("[tw:es] eventsub websocket connected, session: {SessionId}", _client.SessionId);
            
            if (_subscriptions.Count > 0)
            {
                _logger.LogInformation("[tw:es] restoring {Count} eventsub subscriptions", _subscriptions.Count);
                await ResubscribeChannelsAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            return;
            
        await _client.DisconnectAsync().ConfigureAwait(false);
        IsConnected = false;
        _subscriptions.Clear();
    }

    public async Task SubscribeChannelAsync(string channel, CancellationToken cancellationToken = default)
    {
        var normalizedChannel = channel.TrimStart('#').ToLowerInvariant();
        await _subscriptionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SubscribeChannelCoreAsync(channel, normalizedChannel, force: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    private async Task SubscribeChannelCoreAsync(
        string channel,
        string normalizedChannel,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!force && _subscriptions.Contains(normalizedChannel))
            return;
        
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var broadcaster = await _api.Helix.Users.GetUsersAsync(logins: [channel]).ConfigureAwait(false);
        var broadcasterId = broadcaster.Users.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException($"channel #{channel} was not found");
            
        var broadcasterToken = await _repository.GetDataAsync($"twitch:broadcaster_token:{broadcasterId}").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(broadcasterToken))
            throw new InvalidOperationException("broadcaster authorization is unavailable");

        _api.Settings.ClientId = _configuration.ClientId;
        var botAccessToken = await _tokens.GetUserAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        _api.Settings.AccessToken = botAccessToken;

        try
        {
            await _api.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.chat.message", "1",
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = broadcasterId,
                    ["user_id"] = _botUserId
                },
                EventSubTransportMethod.Websocket,
                _client.SessionId,
                accessToken: botAccessToken).ConfigureAwait(false);
                
            _logger.LogInformation("[tw:es] successfully subscribed to #{Channel}", normalizedChannel);
        }
        catch (HttpRequestException exception) when (exception.Message.Contains("subscription already exists", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[tw:es] eventsub subscription already exists for #{Channel}", normalizedChannel);
        }
        
        _subscriptions.Add(normalizedChannel);
    }

    private async Task ResubscribeChannelsAsync()
    {
        await _subscriptionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var channels = _subscriptions.ToArray();
            _subscriptions.Clear();
            
            foreach (var channel in channels)
            {
                try
                {
                    await SubscribeChannelCoreAsync(channel, channel, force: true, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _subscriptions.Add(channel);
                    _logger.LogWarning(exception, "[tw:es] failed to restore eventsub subscription for #{Channel}", channel);
                }
            }
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    public async Task JoinChannelAsync(string channel, CancellationToken cancellationToken = default)
    {
        await SubscribeChannelAsync(channel, cancellationToken);
        await UpdateAndFireUserStateAsync(channel).ConfigureAwait(false);
    }

    public Task JoinChannelViaIrcAsync(string channel, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("eventsub transport cannot join via irc");

    public Task RefreshChannelAsync(string channel, CancellationToken cancellationToken = default) =>
        SubscribeChannelAsync(channel, cancellationToken);

    public Task LeaveChannelAsync(string channel, CancellationToken cancellationToken = default)
    {
        _subscriptions.Remove(channel.TrimStart('#').ToLowerInvariant());
        return Task.CompletedTask;
    }

    public async Task SendMessageAsync(string channel, string message, string? replyToMessageId = null, CancellationToken cancellationToken = default)
    {
        _api.Settings.ClientId = _configuration.ClientId;
        _api.Settings.AccessToken = await _tokens.GetAppAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        
        var users = await _api.Helix.Users.GetUsersAsync(logins: [channel]).ConfigureAwait(false);
        var broadcasterId = users.Users.FirstOrDefault()?.Id ?? throw new InvalidOperationException($"channel #{channel} was not found");
        var senderId = string.IsNullOrWhiteSpace(_botUserId) ? _configuration.BotUserId : _botUserId;
        
        await _api.Helix.Chat.SendChatMessage(new SendChatMessageRequest
        {
            BroadcasterId = broadcasterId,
            SenderId = senderId,
            Message = message,
            ReplyParentMessageId = replyToMessageId
        }, _api.Settings.AccessToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        IsConnected = false;
        _client.ChannelChatMessage -= OnChatMessage;
        _client.UserWhisperMessage -= OnWhisperMessage;
        _client.WebsocketConnected -= OnWebsocketConnected;
        _client.WebsocketReconnected -= OnWebsocketReconnected;
        _client.WebsocketDisconnected -= OnWebsocketDisconnected;
        
        try
        {
            await _client.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "[tw:es] disconnect failed during disposal");
        }
        
        _subscriptionLock.Dispose();
        _connectionLock.Dispose();
    }

    private Task OnWebsocketConnected(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketConnectedArgs args) =>
        ResubscribeChannelsAsync();

    private Task OnWebsocketReconnected(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketReconnectedArgs args) =>
        ResubscribeChannelsAsync();

    private Task OnWebsocketDisconnected(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketDisconnectedArgs args)
    {
        IsConnected = false;
        _logger.LogWarning("[tw:es] websocket disconnected. data={Reason}", args);
        return Task.CompletedTask;
    }

    private Task OnChatMessage(object? sender, ChannelChatMessageArgs args)
    {
        var message = args.Payload.Event;
        MessageReceived?.Invoke(this, new OnMessageReceivedArgs
        {
            ChatMessage = new ChatMessage
            {
                Username = message.ChatterUserName,
                UserId = message.ChatterUserId,
                MessageId = message.MessageId,
                Message = message.Message.Text,
                Channel = message.BroadcasterUserLogin,
                ChannelId = message.BroadcasterUserId,
                IsModerator = message.IsModerator,
                IsBroadcaster = message.IsBroadcaster,
                IsSubscriber = message.IsSubscriber,
                IsVip = message.IsVip,
                Color = message.Color
            }
        });
        return Task.CompletedTask;
    }

    private Task OnWhisperMessage(object? sender, TwitchLib.EventSub.Core.EventArgs.User.UserWhisperMessageArgs args)
    {
        var message = args.Payload.Event;
        MessageReceived?.Invoke(this, new OnMessageReceivedArgs
        {
            ChatMessage = new ChatMessage
            {
                Username = message.FromUserLogin,
                UserId = message.FromUserId,
                MessageId = message.WhisperId,
                Message = message.Whisper.Text,
                Channel = $"whisper:{message.FromUserId}",
                ChannelId = "whisper",
                IsModerator = false,
                IsBroadcaster = false,
                IsSubscriber = false,
                IsVip = false,
                Color = "#ffffff"
            }
        });

        return Task.CompletedTask;
    }
    
    private async Task UpdateAndFireUserStateAsync(string channel)
    {
        try
        {
            var normalizedChannel = channel.TrimStart('#').ToLowerInvariant();
            var broadcaster = await _api.Helix.Users.GetUsersAsync(logins: [normalizedChannel]).ConfigureAwait(false);
            var broadcasterId = broadcaster.Users.FirstOrDefault()?.Id;
            
            if (string.IsNullOrWhiteSpace(broadcasterId) || string.IsNullOrWhiteSpace(_botUserId))
                return;

            // Check moderator status
            var mods = await _api.Helix.Moderation.GetModeratorsAsync(broadcasterId, userIds: [_botUserId]).ConfigureAwait(false);
            var isMod = mods.Data.Length != 0;

            // Check VIP status
            var vips = await _api.Helix.Channels.GetVIPsAsync(broadcasterId).ConfigureAwait(false);
            var isVip = vips.Data.Any(v => v.UserId == _botUserId);

            UserStateChanged?.Invoke(this, new OnUserStateChangedArgs
            {
                Channel = normalizedChannel,
                IsModerator = isMod,
                IsVip = isVip
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[tw:es] failed to fetch initial user state for #{Channel}", channel);
        }
    }
}