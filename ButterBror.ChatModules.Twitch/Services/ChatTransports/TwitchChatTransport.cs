using System.Collections.Concurrent;
using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Interfaces;
using Microsoft.Extensions.Logging;

namespace ButterBror.ChatModules.Twitch.Services.ChatTransports;

public sealed class TwitchChatTransportStrategy : ITwitchChatTransport
{
    private readonly EventSubChatTransport _eventSub;
    private readonly IrcChatTransport _irc;
    private readonly ITwitchTokenManager _tokenManager;
    private readonly ILogger<TwitchChatTransportStrategy> _logger;
    
    private readonly ConcurrentDictionary<string, ITwitchChatTransport> _transports = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _stateLock = new();
    private readonly SemaphoreSlim _transportSwitchLock = new(1, 1);
    
    private ITwitchChatTransport? _primary;
    private bool _isMigrating;

    public string Name => "dynamic";
    
    public bool IsConnected 
    { 
        get 
        {
            lock (_stateLock)
            {
                return _primary?.IsConnected == true || _transports.Values.Any(t => t.IsConnected);
            }
        } 
    }
    
    public IReadOnlyCollection<string> ConnectedChannels => _transports.Keys.ToArray();
    
    public event EventHandler<OnMessageReceivedArgs>? MessageReceived;
    public event EventHandler<OnUserStateChangedArgs>? UserStateChanged;
    public event EventHandler<EventArgs>? TransportFailed;

    public TwitchChatTransportStrategy(
        EventSubChatTransport eventSub,
        IrcChatTransport irc,
        ITwitchTokenManager tokenManager,
        ILogger<TwitchChatTransportStrategy> logger)
    {
        _eventSub = eventSub;
        _irc = irc;
        _tokenManager = tokenManager;
        _logger = logger;
        
        SubscribeEvents(_eventSub);
        SubscribeEvents(_irc);
        
        _eventSub.TransportFailed += OnEventSubTransportFailed;
        _irc.TransportFailed += OnIrcTransportFailed;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _eventSub.ConnectAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock) _primary = _eventSub;
            _logger.LogInformation("[tw] eventsub chat transport connected");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[tw] eventsub unavailable; irc is the active fallback transport");
            await _irc.ConnectAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock) _primary = _irc;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _eventSub.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await _irc.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        _transports.Clear();
        
        lock (_stateLock) _primary = null;
    }

    public async Task JoinChannelAsync(string channel, CancellationToken cancellationToken = default)
    {
        var normalized = channel.TrimStart('#').ToLowerInvariant();
        lock (_stateLock)
        {
            if (_transports.ContainsKey(normalized))
                return;
        }

        var transport = await SelectTransportAsync(normalized, cancellationToken).ConfigureAwait(false);
        _transports[normalized] = transport;

        try
        {
            await transport.JoinChannelAsync(normalized, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (transport == _eventSub)
        {
            await SwitchToIrcAsync(normalized, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw] failed to join #{Channel} via {Transport}", normalized, transport.Name);
            
            lock (_stateLock)
                _transports.TryRemove(normalized, out _);
        }
    }

    public async Task JoinChannelViaIrcAsync(string channel, CancellationToken cancellationToken = default)
    {
        var normalized = channel.TrimStart('#').ToLowerInvariant();
        if (_transports.TryGetValue(normalized, out var existing) && existing == _eventSub)
        {
            await _eventSub.LeaveChannelAsync(normalized, cancellationToken).ConfigureAwait(false);
            _transports.TryRemove(normalized, out _);
        }

        await _irc.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _irc.JoinChannelAsync(normalized, cancellationToken).ConfigureAwait(false);
        _transports[normalized] = _irc;
    }

    public async Task LeaveChannelAsync(string channel, CancellationToken cancellationToken = default)
    {
        var normalized = channel.TrimStart('#').ToLowerInvariant();
        if (_transports.TryRemove(normalized, out var transport))
            await transport.LeaveChannelAsync(normalized, cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshChannelAsync(string channel, CancellationToken cancellationToken = default)
    {
        var normalized = channel.TrimStart('#').ToLowerInvariant();
        if (_transports.TryRemove(normalized, out var current) && current == _irc)
            await _irc.LeaveChannelAsync(normalized, cancellationToken).ConfigureAwait(false);
        await JoinChannelAsync(normalized, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendMessageAsync(string channel, string message, string? replyToMessageId = null, CancellationToken cancellationToken = default)
    {
        var normalized = channel.TrimStart('#').ToLowerInvariant();
        var transport = await SelectTransportAsync(normalized, cancellationToken).ConfigureAwait(false);
        try
        {
            await transport.SendMessageAsync(normalized, message, replyToMessageId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (transport == _eventSub)
        {
            _logger.LogWarning(exception, "[tw] eventsub send failed for #{Channel}; switching channel to irc", normalized);
            await SwitchToIrcAsync(normalized, cancellationToken).ConfigureAwait(false);
            await _irc.SendMessageAsync(normalized, message, replyToMessageId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _eventSub.MessageReceived -= ForwardMessage;
        _eventSub.UserStateChanged -= ForwardUserStateChanged;
        _eventSub.TransportFailed -= OnEventSubTransportFailed;
        
        _irc.MessageReceived -= ForwardMessage;
        _irc.UserStateChanged -= ForwardUserStateChanged;
        _irc.TransportFailed -= OnIrcTransportFailed;
        
        await _eventSub.DisposeAsync().ConfigureAwait(false);
        await _irc.DisposeAsync().ConfigureAwait(false);
    }

    private async void OnEventSubTransportFailed(object? sender, EventArgs e)
    {
        try
        {
            _logger.LogError("[tw] eventsub transport failed. migrating affected channels to irc fallback.");
            await MigrateChannelsAsync(_eventSub, _irc).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "[tw] unhandled exception during eventsub failover");
        }
    }

    private async void OnIrcTransportFailed(object? sender, EventArgs e)
    {
        try
        {
            _logger.LogError("[tw] irc transport failed. migrating affected channels to eventsub fallback.");
            await MigrateChannelsAsync(_irc, _eventSub).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "[tw] unhandled exception during irc failover");
        }
    }

    private async Task MigrateChannelsAsync(ITwitchChatTransport fromTransport, ITwitchChatTransport toTransport)
    {
        lock (_stateLock)
        {
            if (_isMigrating) return;
            _isMigrating = true;
        }

        try
        {
            var channelsToMigrate = _transports
                .Where(kv => kv.Value == fromTransport)
                .Select(kv => kv.Key)
                .ToList();

            if (channelsToMigrate.Count == 0)
            {
                lock (_stateLock)
                {
                    if (_primary == fromTransport) _primary = toTransport;
                }
                return;
            }

            try
            {
                if (!toTransport.IsConnected)
                    await toTransport.ConnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "[tw] fallback transport {Transport} is also down. failover aborted", toTransport.Name);
                TransportFailed?.Invoke(this, EventArgs.Empty);
                return;
            }

            foreach (var channel in channelsToMigrate)
            {
                try
                {
                    await toTransport.JoinChannelAsync(channel).ConfigureAwait(false);
                    _transports[channel] = toTransport;
                    _logger.LogInformation("[tw] successfully migrated #{Channel} to {Transport}", channel, toTransport.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[tw] failed to migrate #{Channel} to {Transport}", channel, toTransport.Name);
                }
            }

            lock (_stateLock)
            {
                if (_primary == fromTransport) _primary = toTransport;
            }
        }
        finally
        {
            lock (_stateLock)
            {
                _isMigrating = false;
            }
        }
    }

    private async Task<ITwitchChatTransport> SelectTransportAsync(string channel, CancellationToken cancellationToken)
    {
        if (_transports.TryGetValue(channel, out var selected))
            return selected;

        await _transportSwitchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_transports.TryGetValue(channel, out selected))
                return selected;

            lock (_stateLock)
            {
                if (_primary == _irc)
                {
                    _transports[channel] = _irc;
                    return _irc;
                }
            }

            try
            {
                await _eventSub.SubscribeChannelAsync(channel, cancellationToken).ConfigureAwait(false);
                _transports[channel] = _eventSub;
            }
            catch (Exception exception)
            {
                _logger.LogWarning("[tw] eventsub subscription denied for #{Channel}; using irc. reason='{Message}'", 
                    channel, exception.Message);
                await SwitchToIrcAsync(channel, cancellationToken).ConfigureAwait(false);
            }

            return _transports[channel];
        }
        finally
        {
            _transportSwitchLock.Release();
        }
    }

    private async Task SwitchToIrcAsync(string channel, CancellationToken cancellationToken)
    {
        await _irc.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _irc.JoinChannelAsync(channel, cancellationToken).ConfigureAwait(false);
        _transports[channel] = _irc;
    }

    private void ForwardMessage(object? sender, OnMessageReceivedArgs args) => MessageReceived?.Invoke(this, args);
    private void ForwardUserStateChanged(object? sender, OnUserStateChangedArgs args) => UserStateChanged?.Invoke(this, args);
    
    private void SubscribeEvents(ITwitchChatTransport transport)
    {
        transport.MessageReceived += ForwardMessage;
        transport.UserStateChanged += ForwardUserStateChanged;
    }
}