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
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);
    
    private string _botUserId = string.Empty;
    private bool _disposed;
    private Task? _reconnectTask;
    private int _reconnectAttempts;

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
        client.ErrorOccurred += OnErrorOccurred;
    }

    public string Name => "eventsub";
    public bool IsConnected { get; private set; }
    public IReadOnlyCollection<string> ConnectedChannels => _subscriptions.ToArray();
    
    public event EventHandler<EventArgs>? TransportFailed;
    public event EventHandler<OnMessageReceivedArgs>? MessageReceived;
    public event EventHandler<OnUserStateChangedArgs>? UserStateChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _botUserId = _configuration.BotUserId;
        _api.Settings.ClientId = _configuration.ClientId;
        _api.Settings.AccessToken = await _tokens.GetUserAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
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

            _logger.LogInformation("[tw:es] connecting...");
            await _client.ConnectAsync(new Uri("wss://eventsub.wss.twitch.tv/ws")).ConfigureAwait(false);
            _reconnectAttempts = 0;
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

    private async Task SubscribeToWhispersAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_botUserId))
        {
            _logger.LogWarning("[tw:es] cannot subscribe to whispers: bot user id is not set");
            return;
        }

        try
        {
            _logger.LogInformation("[tw:es] subscribing to whispers for bot user {BotUserId}", _botUserId);
            var botAccessToken = await _tokens.GetUserAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            
            await _api.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "user.whisper.message", "1",
                new Dictionary<string, string> { ["user_id"] = _botUserId },
                EventSubTransportMethod.Websocket,
                _client.SessionId,
                accessToken: botAccessToken
            ).ConfigureAwait(false);
            
            _logger.LogInformation("[tw:es] successfully subscribed to whispers");
        }
        catch (HttpRequestException exception) when (exception.Message.Contains("subscription already exists", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("[tw:es] whisper subscription already exists");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw:es] failed to subscribe to whispers");
        }
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
            _logger.LogWarning("[tw:es] subscription already exists for #{Channel}", normalizedChannel);
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
                    _logger.LogWarning(exception, "[tw:es] failed to restore subscription for #{Channel}", channel);
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
        _disposed = true;
        IsConnected = false;
        
        _client.ChannelChatMessage -= OnChatMessage;
        _client.UserWhisperMessage -= OnWhisperMessage;
        _client.WebsocketConnected -= OnWebsocketConnected;
        _client.WebsocketReconnected -= OnWebsocketReconnected;
        _client.WebsocketDisconnected -= OnWebsocketDisconnected;
        _client.ErrorOccurred -= OnErrorOccurred;
        
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
        _reconnectLock.Dispose();
    }

    private Task OnWebsocketConnected(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketConnectedArgs args)
    {
        IsConnected = true;
        _logger.LogInformation("[tw:es] connected. session: {SessionId}", _client.SessionId);

        _ = Task.Run(async () =>
        {
            try
            {
                if (_subscriptions.Count > 0)
                    await ResubscribeChannelsAsync().ConfigureAwait(false);
                
                await SubscribeToWhispersAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[tw:es] error during post-connection subscriptions setup");
            }
        });

        return Task.CompletedTask;
    }
    
    private Task OnWebsocketReconnected(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketReconnectedArgs args)
    {
        IsConnected = true;
        _logger.LogInformation("[tw:es] successfully reconnected. session: {SessionId}", _client.SessionId);
        
        return Task.CompletedTask;
    }

    private Task OnWebsocketDisconnected(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketDisconnectedArgs args)
    {
        IsConnected = false;
        _logger.LogWarning("[tw:es] disconnected. triggering internal reconnect loop.");
        TriggerReconnect("websocket disconnected");
        
        return Task.CompletedTask;
    }
    
    private Task OnErrorOccurred(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.ErrorOccuredArgs args)
    {
        IsConnected = false;
        _logger.LogError(args.Exception, "[tw:es] error occurred: {Message}", args.Message);
        TriggerReconnect("error occurred");
        
        return Task.CompletedTask;
    }
    
    private void TriggerReconnect(string reason)
    {
        if (_disposed) return;
        
        if (_reconnectTask is { IsCompleted: false }) return;

        _reconnectTask = Task.Run(() => ReconnectLoopAsync(reason));
    }
    
    private async Task ReconnectLoopAsync(string reason)
    {
        if (!await _reconnectLock.WaitAsync(0).ConfigureAwait(false)) return;

        try
        {
            _logger.LogWarning("[tw:es] starting reconnect loop. reason: {Reason}", reason);

            while (!_disposed && !IsConnected)
            {
                _reconnectAttempts++;
                var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, _reconnectAttempts))) +
                            TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));

                _logger.LogInformation("[tw:es] reconnect attempt {Attempt} in {Delay}s", _reconnectAttempts, delay.TotalSeconds);
                await Task.Delay(delay).ConfigureAwait(false);

                try
                {
                    var success = await _client.ConnectAsync(new Uri("wss://eventsub.wss.twitch.tv/ws")).ConfigureAwait(false);
                    if (success)
                    {
                        _logger.LogInformation("[tw:es] successfully reconnected");
                        _reconnectAttempts = 0;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[tw:es] reconnect attempt {Attempt} failed", _reconnectAttempts);
                }

                if (_reconnectAttempts < 10)
                    continue;
                
                _logger.LogError("[tw:es] max reconnect attempts reached. triggering transport failover");
                TransportFailed?.Invoke(this, EventArgs.Empty);
                return;
            }
        }
        finally
        {
            _reconnectLock.Release();
        }
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
            
            var mods = await _api.Helix.Moderation.GetModeratorsAsync(broadcasterId, userIds: [_botUserId]).ConfigureAwait(false);
            var isMod = mods.Data.Length != 0;
            
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