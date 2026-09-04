using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Interfaces;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ButterBror.ChatModules.Twitch.Services;

public sealed class TwitchNotificationService : ITwitchNotificationService
{
    private readonly ITwitchChatTransport _chatTransport;
    private readonly IOptions<TwitchConfiguration> _options;
    private readonly ILocalizationService _localization;
    private readonly ILogger<TwitchNotificationService> _logger;
    private readonly TwitchMessageRender _messageRender;
    private ITwitchClient? _twitchClient;

    public TwitchNotificationService(
        ITwitchChatTransport chatTransport,
        IOptions<TwitchConfiguration> options,
        ILocalizationService localization,
        TwitchMessageRender render,
        ILogger<TwitchNotificationService> logger)
    {
        _chatTransport = chatTransport;
        _options = options;
        _localization = localization;
        _logger = logger;
        _messageRender = render;

        _chatTransport.TransportConnected += OnTransportConnected;
        _chatTransport.TransportReconnected += OnTransportReconnected;
    }

    public void SetClient(ITwitchClient client) => _twitchClient = client;

    public Task NotifyChannelJoinedAsync(string channel, string executor, CancellationToken cancellationToken = default)
        => SendNotificationSafeAsync("notification.channel.join", _options.Value.Notifications.ChannelJoin, channel, executor);

    public Task NotifyChannelPartedAsync(string channel, string executor, CancellationToken cancellationToken = default)
        => SendNotificationSafeAsync("notification.channel.part", _options.Value.Notifications.ChannelPart, channel, executor);

    public Task NotifyChannelAddedAsync(string channel, string executor = "system", CancellationToken cancellationToken = default)
        => SendNotificationSafeAsync("notification.channel.add", _options.Value.Notifications.ChannelAdd, channel, executor);
    
    public Task NotifyChannelRemovedAsync(string channel, string executor, CancellationToken cancellationToken = default)
        => SendNotificationSafeAsync("notification.channel.remove", _options.Value.Notifications.ChannelRemove, channel, executor);

    private void OnTransportConnected(object? sender, TransportConnectionEventArgs e)
    {
        var settings = _options.Value.Notifications;
        var (eventSettings, key) = e.TransportName == "irc"
            ? (settings.IrcConnect, "notification.irc.connect")
            : (settings.EventSubConnect, "notification.eventsub.connect");

        _ = SendNotificationSafeAsync(key, eventSettings);
    }

    private void OnTransportReconnected(object? sender, TransportConnectionEventArgs e)
    {
        var settings = _options.Value.Notifications;
        var (eventSettings, key) = e.TransportName == "irc"
            ? (settings.IrcReconnect, "notification.irc.reconnect")
            : (settings.EventSubReconnect, "notification.eventsub.reconnect");

        _ = SendNotificationSafeAsync(key, eventSettings);
    }

    private Task SendNotificationSafeAsync(string key, TwitchNotificationEventSettings eventSettings, string channelLogin = "", string executor = "")
        => Task.Run(async () =>
        {
            try
            {
                await SendNotificationAsync(key, eventSettings, channelLogin, executor).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[tw:notif] unhandled exception sending notification {Key}", key);
            }
        });

    private async Task SendNotificationAsync(string key, TwitchNotificationEventSettings eventSettings, string channelLogin = "", string executor = "")
    {
        var config = _options.Value;
        if (!config.Notifications.Enabled || !eventSettings.Enabled)
            return;

        var client = _twitchClient;
        if (client is null)
        {
            _logger.LogWarning("[tw:notif] twitch client not initialized, skipping notification {Key}", key);
            return;
        }

        var channels = GetTargetChannels(config, eventSettings).ToList();
        if (channels.Count == 0) return;

        string message;
        try
        {
            message = await _localization.GetStringAsync(key, "EN_US", channelLogin, executor).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw:notif] failed to get localization for key {Key}", key);
            return;
        }

        var resultMessage = _messageRender.RenderTwitchMessageInternal(new Message(message));
        foreach (var channel in channels)
        {
            try
            {
                await client.SendMessageAsync(channel, resultMessage).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[tw:notif] failed to send notification to #{Channel}", channel);
            }
        }
    }

    private static IEnumerable<string> GetTargetChannels(TwitchConfiguration config, TwitchNotificationEventSettings eventSettings)
    {
        var channels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        var defaultChannel = string.IsNullOrWhiteSpace(config.Notifications.DefaultChannel)
            ? config.BotUsername
            : config.Notifications.DefaultChannel;

        if (!string.IsNullOrWhiteSpace(defaultChannel))
            channels.Add(defaultChannel.TrimStart('#').ToLowerInvariant());
        
        foreach (var ch in config.Notifications.GlobalChannels.Where(ch => !string.IsNullOrWhiteSpace(ch)))
            channels.Add(ch.TrimStart('#').ToLowerInvariant());
        
        foreach (var ch in eventSettings.Channels.Where(ch => !string.IsNullOrWhiteSpace(ch)))
            channels.Add(ch.TrimStart('#').ToLowerInvariant());

        return channels;
    }
}