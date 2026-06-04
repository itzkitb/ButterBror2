using ButterBror.ChatModule;
using ButterBror.ChatModules.Twitch.Commands;
using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.ChatModules.Twitch.Services;
using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using ButterBror.Data;
using ButterBror.Domain.Chat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Registry;
using System.Collections.Concurrent;
using System.Text.Json;
using TwitchLib.Api.Helix;
using TwitchLib.Client.Events;

namespace ButterBror.ChatModules.Twitch;

public class TwitchModule : IChatModule
{
    public string ModuleId => "sillyapps:twitch";
    public Version Version => new(1, 0, 1);
    private Func<ICommand> _joinCommandFactory = null!;
    private Func<ICommand> _partCommandFactory = null!;
    private Func<ICommand> _setPrefixCommandFactory = null!;

    public IReadOnlyList<ModuleCommandExport> ExportedCommands => new List<ModuleCommandExport>
    {
        new ModuleCommandExport("join", _joinCommandFactory, new JoinChannelCommandMetadata()),
        new ModuleCommandExport("part", _partCommandFactory, new PartChannelCommandMetadata()),
        new ModuleCommandExport("setprefix", _setPrefixCommandFactory, new SetPrefixCommandMetadata())
    };

    private ITwitchClient _twitchClient = null!;
    private IBotCore _botCore = null!;
    private ILogger<TwitchModule> _logger = null!;
    private TwitchConfiguration _config = null!;
    private ICustomDataRepository _db = null!;
    private IDashboardBridge? _dashboardBridge;

    private readonly ConcurrentDictionary<string, string> _prefixCache
        = new(StringComparer.Ordinal);

    public void InitializeWithServices(IServiceProvider serviceProvider)
    {
        var appDataPathProvider = serviceProvider.GetRequiredService<IAppDataPathProvider>();
        var configService = new TwitchConfigurationService(appDataPathProvider);
        var config = configService.LoadConfiguration();
        var options = Options.Create(config);
        _config = options.Value;

        _db = serviceProvider.GetRequiredService<ICustomDataRepository>();
        _logger = serviceProvider.GetRequiredService<ILogger<TwitchModule>>();
        _dashboardBridge = serviceProvider.GetService<IDashboardBridge>();
        var redisRepo = serviceProvider.GetService<ICustomDataRepository>();

        string irsChannelsString = redisRepo?.GetDataAsync("twitch:irc_channels").Result ?? "[]";
        List<string> ircChannels = JsonSerializer.Deserialize<List<string>>(irsChannelsString) ?? new List<string>();
        string eventSubChannelsString = redisRepo?.GetDataAsync("twitch:eventsub_channels").Result ?? "[]";
        List<string> eventSubChannels = JsonSerializer.Deserialize<List<string>>(eventSubChannelsString) ?? new List<string>();

        _twitchClient = new TwitchLibClient(
            options,
            serviceProvider.GetRequiredService<ResiliencePipelineProvider<string>>(),
            serviceProvider.GetRequiredService<ILogger<TwitchLibClient>>(),
            ircChannels,
            eventSubChannels
        );

        // Updating factories
        _joinCommandFactory = () => new JoinChannelCommand(_twitchClient);
        _partCommandFactory = () => new PartChannelCommand(_twitchClient);
        _setPrefixCommandFactory = () => new SetPrefixCommand(this);
    }

    public async Task InitializeAsync(IBotCore core)
    {
        _botCore = core;
        // S0: Main events
        _twitchClient.OnMessageReceived += OnMessageReceived;
        _twitchClient.OnConnected += OnConnected;
        _twitchClient.OnDisconnected += OnDisconnected;

        // S1: Subscribing to additional features
        if (_twitchClient is TwitchLibClient libClient)
        {
            libClient.OnNewSubscriber += OnNewSubscriber;
            libClient.OnGiftedSubscription += OnGiftedSubscription;
            libClient.OnRaidNotification += OnRaidNotification;
            libClient.OnBitsReceived += OnBitsReceived;
        }

        if (_config.IsEnabled)
        {
            if (string.IsNullOrWhiteSpace(_config.OauthToken))
            {
                _logger.LogError("[TW] OAuth token is missing. Module will not start!");
                return;
            }

            try
            {
                await _twitchClient.ConnectAsync(_config.BotUsername, _config.OauthToken, _config.ClientId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TW] Failed to initialize module");
                throw;
            }
        }
        else
        {
            _logger.LogWarning("[TW] Module is disabled in configuration");
        }
    }

    private async void OnConnected(object? sender, OnConnectedEventArgs e)
    {
        _ = SafeHandleConnectAsync(e).ContinueWith(
            t => _logger.LogError(t.Exception, "[TW] Unhandled exception in connect handler"),
            TaskContinuationOptions.OnlyOnFaulted
        );
    }

    private async Task SafeHandleConnectAsync(OnConnectedEventArgs e)
    {
        _logger.LogInformation("[TW] Connected! My name is {Username}", e.BotUsername);
        if (_twitchClient is TwitchLibClient libClient && libClient.IsConnected)
        {
            await libClient.SendMessageAsync(_config.Channel, $"Bot connected successfully!");
        }
    }

    private async void OnDisconnected(object? sender, OnDisconnectedArgs e)
    {
        _logger.LogWarning("[TW] Disconnected");
    }

    private void OnMessageReceived(object? sender, Events.OnMessageReceivedArgs e)
    {
        _ = SafeHandleMessageAsync(e).ContinueWith(
            t => _logger.LogError(t.Exception, "[TW] Unhandled exception in message handler"),
            TaskContinuationOptions.OnlyOnFaulted
        );
    }

    private async Task SafeHandleMessageAsync(Events.OnMessageReceivedArgs e)
    {
        // Notify dashboard
        _dashboardBridge?.IncrementMessageCount();
        var chatMessage = e.ChatMessage;

        var extra = new TwitchMessageExtra()
        {
            IsModerator = chatMessage.IsModerator,
            IsBroadcaster = chatMessage.IsBroadcaster,
            IsSubscriber = chatMessage.IsSubscriber,
            IsVIP = chatMessage.IsVip,
            Color = chatMessage.Color,
            Channel = chatMessage.Channel,
            ChannelId = chatMessage.ChannelId,
            Badges = chatMessage.Badges
        };

        await _botCore.RaiseMessageReceivedAsync(
            ModuleId,
            new IncomingChatMessage(
                Text: chatMessage.Message,
                ExtraData: extra,
                ReceivedAt: DateTime.UtcNow,
                PlatformUserId: chatMessage.UserId,
                PlatformUserName: chatMessage.Username,
                PlatformChatId: chatMessage.ChannelId,
                PlatformChatName: chatMessage.Channel
            ),
            platform: ModuleId
        );

        var prefix = await GetChannelPrefixAsync(chatMessage.ChannelId);
        if (TryParseCommand(e.ChatMessage.Message, prefix, out var commandName, out var arguments))
        {
            var context = CreateCommandContext(e.ChatMessage, commandName, arguments);
            var result = await _botCore.ProcessCommandAsync(context).ConfigureAwait(false);

            if (!result.SendResult)
            {
                _logger.LogInformation("[TW] Not sent due to reply flag: {result}", result.Message ?? "[]");
                return;
            }

            if (_twitchClient is TwitchLibClient libClient && libClient.IsConnected)
            {
                try
                {
                    await SendResponseAsync(libClient, e.ChatMessage, result.Message ?? "Command executed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TW] Failed to send command result back to #{Channel}", e.ChatMessage.Channel);
                }
            }
        }
    }

    private async Task SendResponseAsync(TwitchLibClient client, ChatMessage triggeringMessage, string responseText)
    {
        switch (_config.ReplyMode)
        {
            case TwitchReplyMode.Reply:
                await client.SendReplyAsync(
                    triggeringMessage.Channel,
                    triggeringMessage.MessageId,
                    responseText
                );
                break;

            case TwitchReplyMode.Mention:
            default:
                var mentionText = $"@{triggeringMessage.Username}, {responseText}";
                await client.SendMessageAsync(triggeringMessage.Channel, mentionText);
                break;
        }
    }

    private bool TryParseCommand(string message, string prefix, out string commandName, out string[] arguments)
    {
        commandName = string.Empty;
        arguments = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(message) || !message.StartsWith(prefix))
        {
            return false;
        }

        string cleanMessage = message.Substring(prefix.Length).TrimStart();

        var parts = cleanMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        commandName = parts[0];
        arguments = parts.Skip(1).ToArray();

        return !string.IsNullOrWhiteSpace(commandName);
    }

    private async Task<string> GetChannelPrefixAsync(string channelId)
    {
        if (_prefixCache.TryGetValue(channelId, out var cached))
            return cached;

        try
        {
            var stored = await _db.GetDataAsync(SetPrefixCommand.GetPrefixKey(channelId));
            var prefix = !string.IsNullOrWhiteSpace(stored)
                ? stored
                : _config.CommandPrefix;

            _prefixCache[channelId] = prefix;
            return prefix;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[TW] Failed to load prefix from Redis for #{ChannelId}, using default '{Default}'",
                channelId, _config.CommandPrefix);
            return _config.CommandPrefix;
        }
    }

    public void InvalidatePrefixCache(string channelId)
        => _prefixCache.TryRemove(channelId, out _);

    private async void OnNewSubscriber(object? sender, Events.OnNewSubscriberArgs e)
    {
        if (_twitchClient is TwitchLibClient libClient && libClient.IsConnected)
        {
            try
            {
                // TODO: Add something here idk
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TW] Failed to send subscriber message");
            }
        }
    }

    private async void OnGiftedSubscription(object? sender, Events.OnGiftedSubscriptionArgs e)
    {
        if (_twitchClient is TwitchLibClient libClient && libClient.IsConnected)
        {
            try
            {
                // TODO: Add something here idk
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TW] Failed to send gifted subscription message");
            }
        }
    }

    private async void OnRaidNotification(object? sender, Events.OnRaidNotificationArgs e)
    {
        if (_twitchClient is TwitchLibClient libClient && libClient.IsConnected)
        {
            try
            {
                // TODO: Add something here idk
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TW] Failed to send raid notification message");
            }
        }
    }

    private async void OnBitsReceived(object? sender, OnBitsReceivedArgs e)
    {
        if (_twitchClient is TwitchLibClient libClient && libClient.IsConnected)
        {
            try
            {
                // TODO: Add something here idk
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TW] Failed to send bits message");
            }
        }
    }

    private ICommandContext CreateCommandContext(ChatMessage chatMessage, string commandName, string[] arguments)
    {
        return new TwitchCommandContext(
            commandName,
            arguments,
            new TwitchUser(
                chatMessage.Username,
                chatMessage.UserId,
                chatMessage.IsModerator,
                chatMessage.IsBroadcaster
            ),
            new TwitchChannel(chatMessage.Channel, chatMessage.ChannelId),
            DateTime.UtcNow
        );
    }

    public async Task ShutdownAsync()
    {
        _twitchClient.OnMessageReceived -= OnMessageReceived;
        _twitchClient.OnConnected -= OnConnected;
        _twitchClient.OnDisconnected -= OnDisconnected;

        if (_twitchClient is TwitchLibClient libClient)
        {
            libClient.OnNewSubscriber -= OnNewSubscriber;
            libClient.OnGiftedSubscription -= OnGiftedSubscription;
            libClient.OnRaidNotification -= OnRaidNotification;
            libClient.OnBitsReceived -= OnBitsReceived;
        }

        await _twitchClient.DisconnectAsync();
        _logger.LogInformation("[TW] Module shutdown complete");
    }

    public class TwitchMessageExtra
    {
        public bool IsModerator { get; internal set; }
        public bool IsBroadcaster { get; internal set; }
        public bool IsSubscriber { get; internal set; }
        public bool IsVIP { get; internal set; }
        public string? Color { get; internal set; }
        public string? Channel { get; internal set; }
        public string? ChannelId { get; internal set; }
        public List<KeyValuePair<string, string>>? Badges { get; internal set; }
    }
}