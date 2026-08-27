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
    private ITwitchChatTransport? _primary;

    public string Name => "dynamic";
    public bool IsConnected => _primary?.IsConnected == true || _transports.Values.Any(t => t.IsConnected);
    public IReadOnlyCollection<string> ConnectedChannels => _transports.Keys.ToArray();
    public event EventHandler<OnMessageReceivedArgs>? MessageReceived;
    public event EventHandler<OnUserStateChangedArgs>? UserStateChanged;

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
        tokenManager.StateChanged += OnTokenStateChanged;
        SubscribeEvents(eventSub);
        SubscribeEvents(irc);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _eventSub.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _primary = _eventSub;
            _logger.LogInformation("[tw] eventsub chat transport connected");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[tw] eventsub unavailable; irc is the active fallback transport");
            await _irc.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _primary = _irc;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _eventSub.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await _irc.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        _transports.Clear();
        _primary = null;
    }

    public async Task JoinChannelAsync(string channel, CancellationToken cancellationToken = default)
    {
        var normalized = channel.TrimStart('#').ToLowerInvariant();
        if (_transports.ContainsKey(normalized))
            return;

        var transport = await SelectTransportAsync(normalized, cancellationToken).ConfigureAwait(false);
        _transports[normalized] = transport;
        await transport.JoinChannelAsync(normalized, cancellationToken).ConfigureAwait(false);
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
        var normalized = channel.TrimStart('#');
        if (_transports.TryRemove(normalized, out var transport))
            await transport.LeaveChannelAsync(normalized, cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshChannelAsync(string channel, CancellationToken cancellationToken = default)
    {
        var normalized = channel.TrimStart('#');
        if (_transports.TryRemove(normalized, out var current) && current == _irc)
            await _irc.LeaveChannelAsync(normalized, cancellationToken).ConfigureAwait(false);
        await JoinChannelAsync(normalized, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendMessageAsync(string channel, string message, string? replyToMessageId = null, CancellationToken cancellationToken = default)
    {
        var normalized = channel.TrimStart('#');
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
        _irc.MessageReceived -= ForwardMessage;
        _irc.UserStateChanged -= ForwardUserStateChanged;
        
        await _eventSub.DisposeAsync().ConfigureAwait(false);
        await _irc.DisposeAsync().ConfigureAwait(false);
        _tokenManager.StateChanged -= OnTokenStateChanged;
    }

    private async Task<ITwitchChatTransport> SelectTransportAsync(string channel, CancellationToken cancellationToken)
    {
        if (_transports.TryGetValue(channel, out var selected))
            return selected;

        if (_primary == _irc)
            _transports[channel] = _irc;
        else
        {
            try
            {
                await _eventSub.SubscribeChannelAsync(channel, cancellationToken).ConfigureAwait(false);
                _transports[channel] = _eventSub;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "[tw] eventsub subscription denied for #{Channel}; using irc", channel);
                await SwitchToIrcAsync(channel, cancellationToken).ConfigureAwait(false);
            }
        }

        return _transports[channel];
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

    private void OnTokenStateChanged(object? sender, TwitchTokenState state)
    {
        _ = Task.Run(async () =>
        {
            if (state.BotCredential is null)
            {
                await DisconnectAsync().ConfigureAwait(false);
            }
        });
    }
}
