using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Interfaces;
using ButterBror.ChatModules.Twitch.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using ChatMessage = ButterBror.ChatModules.Twitch.Models.ChatMessage;

namespace ButterBror.ChatModules.Twitch.Services.ChatTransports;

public sealed class IrcChatTransport(
    IOptions<TwitchConfiguration> options,
    ITwitchTokenManager tokenManager,
    ILogger<IrcChatTransport> logger)
    : ITwitchChatTransport
{
    private readonly TwitchConfiguration _configuration = options.Value;
    
    private readonly Lock _syncLock = new(); 
    private readonly HashSet<string> _desiredChannels = new(StringComparer.OrdinalIgnoreCase);

    private TwitchLib.Client.TwitchClient? _client;
    private WebSocketClient? _socket;

    private TaskCompletionSource? _connectedTcs;
    private CancellationTokenSource? _lifecycleCts;
    private Task? _heartbeatTask;
    private Task? _reconnectTask;
    
    private long _lastDataReceivedTicks;
    private int _reconnectAttempts;
    private bool _disposed;
    private volatile bool _isDisconnecting;

    public string Name => "irc";
    public bool IsConnected => _client?.IsConnected == true;
    public IReadOnlyCollection<string> ConnectedChannels => _client?.JoinedChannels.Select(c => c.Channel).ToArray() ?? Array.Empty<string>();

    public event EventHandler<OnMessageReceivedArgs>? MessageReceived;
    public event EventHandler<OnUserStateChangedArgs>? UserStateChanged;
    public event EventHandler<EventArgs>? TransportFailed;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (_lifecycleCts is not null)
            await _lifecycleCts.CancelAsync().ConfigureAwait(false);
            
        _lifecycleCts = new CancellationTokenSource();
        _reconnectAttempts = 0;

        await ConnectCoreAsync(_lifecycleCts.Token).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_lifecycleCts is not null)
            await _lifecycleCts.CancelAsync().ConfigureAwait(false);
        
        try
        {
            if (_client?.IsConnected == true)
                await _client.DisconnectAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[tw:irc] error during graceful disconnect");
        }
    }

    public async Task JoinChannelAsync(string channel, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeChannel(channel);
        
        lock (_syncLock)
            _desiredChannels.Add(normalized);

        if (_client?.IsConnected == true)
            await _client.JoinChannelAsync(normalized).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task JoinChannelViaIrcAsync(string channel, CancellationToken cancellationToken = default) => 
        JoinChannelAsync(channel, cancellationToken);

    public async Task LeaveChannelAsync(string channel, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeChannel(channel);
        
        lock (_syncLock)
            _desiredChannels.Remove(normalized);

        if (_client?.IsConnected == true)
            await _client.LeaveChannelAsync(normalized).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task RefreshChannelAsync(string channel, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task SendMessageAsync(string channel, string message, string? replyToMessageId = null, CancellationToken cancellationToken = default)
    {
        var client = EnsureClient();
        var task = replyToMessageId is null
            ? client.SendMessageAsync(channel, message)
            : client.SendReplyAsync(channel, replyToMessageId, message);

        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_lifecycleCts is not null)
            await _lifecycleCts.CancelAsync().ConfigureAwait(false);
            
        await DisconnectAndDisposeCurrentClient();
    }

    private async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        await DisconnectAndDisposeCurrentClient();

        var token = await tokenManager.GetUserAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        (_client, _socket) = CreateClient();

        _connectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        _client.Initialize(new ConnectionCredentials(_configuration.BotUsername, $"oauth:{token}", disableUsernameCheck: true));
        Interlocked.Exchange(ref _lastDataReceivedTicks, DateTimeOffset.UtcNow.UtcTicks);

        await _client.ConnectAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await _connectedTcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("[tw:irc] timeout waiting for OnConnected event");
        }

        await RejoinDesiredChannelsAsync(cancellationToken).ConfigureAwait(false);

        _heartbeatTask = Task.Run(() => RunHeartbeatAsync(cancellationToken), cancellationToken);
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);

                var socket = _socket;
                if (socket is null) return;

                var pingSentAt = DateTimeOffset.UtcNow.UtcTicks;

                try
                {
                    await socket.SendAsync("PING :keepalive\r\n");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[tw:irc] failed to send ping");
                    await TriggerReconnect("ping send failed");
                    return;
                }
                
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);

                if (Interlocked.Read(ref _lastDataReceivedTicks) < pingSentAt)
                {
                    logger.LogWarning("[tw:irc] pong timeout detected. forcing reconnect");
                    await TriggerReconnect("pong timeout");
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private Task TriggerReconnect(string reason)
    {
        lock (_syncLock)
        {
            if (_reconnectTask is { IsCompleted: false })
                return Task.CompletedTask;
            _reconnectTask = Task.Run(() => ReconnectLoopAsync(reason));
        }
        
        return Task.CompletedTask;
    }

    private async Task ReconnectLoopAsync(string reason)
    {
        logger.LogWarning("[tw:irc] starting reconnect loop. reason: {Reason}", reason);

        try
        {
            while (!_disposed && _lifecycleCts?.IsCancellationRequested == false)
            {
                _reconnectAttempts++;
                var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, _reconnectAttempts))) +
                            TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));

                await Task.Delay(delay, _lifecycleCts!.Token).ConfigureAwait(false);

                try
                {
                    await ConnectCoreAsync(_lifecycleCts.Token).ConfigureAwait(false);
                    _reconnectAttempts = 0;
                    logger.LogInformation("[tw:irc] successfully reconnected");
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[tw:irc] reconnect attempt {Attempt} failed", _reconnectAttempts);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private (TwitchLib.Client.TwitchClient client, WebSocketClient socket) CreateClient()
    {
        var options = new ClientOptions(new ReconnectionPolicy(3000, 10000, int.MaxValue));
        var socket = new WebSocketClient(options);
        
        socket.OnMessage += OnSocketData;

        var client = new TwitchLib.Client.TwitchClient(socket);
        client.OnMessageReceived += OnMessageReceived;
        client.OnUserStateChanged += OnUserStateChanged;
        
        client.OnConnected += OnClientConnected;
        client.OnDisconnected += OnClientDisconnected;
        client.OnConnectionError += OnConnectionError;

        return (client, socket);
    }

    private Task OnSocketData(object? sender, TwitchLib.Communication.Events.OnMessageEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Message))
            Interlocked.Exchange(ref _lastDataReceivedTicks, DateTimeOffset.UtcNow.UtcTicks);
        
        return Task.CompletedTask;
    }
    
    private Task OnClientConnected(object? sender, TwitchLib.Client.Events.OnConnectedEventArgs e)
    {
        _connectedTcs?.TrySetResult();
        
        return Task.CompletedTask;
    }
    
    private Task OnClientDisconnected(object? sender, TwitchLib.Client.Events.OnDisconnectedArgs e)
    {
        return _isDisconnecting ? Task.CompletedTask : TriggerReconnect("client disconnected");
    }

    private Task OnConnectionError(object? sender, TwitchLib.Client.Events.OnConnectionErrorArgs e)
    {
        return _isDisconnecting ? Task.CompletedTask : TriggerReconnect("connection error");
    }
    
    private async Task RejoinDesiredChannelsAsync(CancellationToken cancellationToken)
    {
        string[] channels;
        lock (_syncLock)
            channels = _desiredChannels.ToArray();

        if (_client is null || channels.Length == 0) return;

        foreach (var channel in channels)
        {
            try
            {
                await _client.JoinChannelAsync(channel).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[tw:irc] failed to rejoin channel #{Channel}", channel);
            }
        }
    }

    private async Task DisconnectAndDisposeCurrentClient()
    {
        if (_client is not null)
        {
            _isDisconnecting = true;
            try 
            { 
                await _client.DisconnectAsync().ConfigureAwait(false); 
            } 
            catch (Exception ex) 
            { 
                logger.LogDebug(ex, "[tw:irc] error force-disposing client"); 
            }
            finally
            {
                _isDisconnecting = false;
            }

            _client.OnMessageReceived -= OnMessageReceived;
            _client.OnUserStateChanged -= OnUserStateChanged;
            _client.OnConnected -= OnClientConnected;
            _client.OnDisconnected -= OnClientDisconnected;
            _client.OnConnectionError -= OnConnectionError;
            
            _client = null;
        }

        if (_socket is not null)
        {
            _socket.OnMessage -= OnSocketData;
            (_socket as IDisposable)?.Dispose();
            _socket = null;
        }
    }

    private Task OnMessageReceived(object? sender, TwitchLib.Client.Events.OnMessageReceivedArgs args)
    {
        var message = args.ChatMessage;
        MessageReceived?.Invoke(this, new OnMessageReceivedArgs
        {
            ChatMessage = new ChatMessage
            {
                Username = message.Username, UserId = message.UserId, MessageId = message.Id,
                Message = message.Message, Channel = message.Channel, ChannelId = message.RoomId,
                IsModerator = message.UserDetail.IsModerator, IsBroadcaster = message.IsBroadcaster,
                IsSubscriber = message.UserDetail.IsSubscriber, IsVip = message.UserDetail.IsVip,
                Badges = message.Badges, Color = message.HexColor
            }
        });
        
        return Task.CompletedTask;
    }

    private Task OnUserStateChanged(object? sender, TwitchLib.Client.Events.OnUserStateChangedArgs args)
    {
        UserStateChanged?.Invoke(this, new OnUserStateChangedArgs
        {
            Channel = args.UserState.Channel.ToLowerInvariant(),
            IsModerator = args.UserState.IsModerator,
            IsVip = args.UserState.Badges.Any(b => b.Key.Equals("vip", StringComparison.OrdinalIgnoreCase))
        });
        
        return Task.CompletedTask;
    }

    private TwitchLib.Client.TwitchClient EnsureClient() => _client ?? throw new InvalidOperationException("irc client is not initialized");
    private static string NormalizeChannel(string channel) => channel.TrimStart('#').ToLowerInvariant();
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}