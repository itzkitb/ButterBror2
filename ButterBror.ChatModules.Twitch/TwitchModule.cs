using System.Collections.Concurrent;
using ButterBror.ChatModules.Twitch.Commands;
using ButterBror.ChatModules.Twitch.Interfaces;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.ChatModules.Twitch.Services;
using ButterBror.ChatModules.Twitch.Services.Auth;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Messaging;
using ButterBror.Core.Modules;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Enums;
using ButterBror.Data.Interfaces;
using ButterBror.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Registry;
using ChatMessage = ButterBror.Domain.Chat.ChatMessage;

namespace ButterBror.ChatModules.Twitch;

public class TwitchModule : IChatModule
{
    // ><> metadata
    public string ModuleId => "sillyapps:twitch";
    public Version Version { get; } = new(1, 5, 6);
    public List<ChatModuleFlags> Flags { get; } = [ ChatModuleFlags.CanSendMessages ];

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> DefaultTranslations =>
        Services.Localization.DefaultTranslations;

    // ><> state & dependencies
    private readonly ConcurrentDictionary<string, string> _prefixCache = new(StringComparer.Ordinal);
    private TwitchMessageRender? _messageRender;

    private ILogger<TwitchModule> _logger = null!;
    private IBotCore _botCore = null!;
    private TwitchConfiguration _config = null!;
    private ICustomDataRepository _db = null!;
    private IDashboardBridge? _dashboardBridge;
    private ILocalizationService? _localization;

    private TwitchClient _twitchClient = null!;
    private IServiceProvider? _moduleServiceProvider;
    private ITwitchTokenManager? _tokenManager;
    private TwitchAuthFileWatcher? _authWatcher;
    private TwitchTokenRefreshBackgroundService? _tokenRefreshService;
    private TwitchAuthPollingService? _authPollingService;
    private TwitchBroadcasterService _broadcasterService = null!;
    private CommandFactories _commandFactories = null!;

    public bool IsInitialized { get; private set; }
    public bool IsConnected => _twitchClient.IsConnected;
    public IReadOnlyList<ModuleCommandExport> ExportedCommands => _commandFactories.Export;

    // ><> lifecycle
    public async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        if (IsInitialized)
            return;

        var config = await LoadConfigurationAsync(serviceProvider);
        if (!config.IsEnabled)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<TwitchModule>>();
            logger.LogInformation("[tw] module is disabled");
            return;
        }

        ResolveCoreDependencies(serviceProvider, config);
        
        _moduleServiceProvider = CreateModuleServiceProvider(serviceProvider, config);
        _tokenManager = _moduleServiceProvider.GetRequiredService<ITwitchTokenManager>();
        await _tokenManager.InitializeAsync();
        _tokenRefreshService = _moduleServiceProvider.GetRequiredService<TwitchTokenRefreshBackgroundService>();
        await _tokenRefreshService.StartAsync(CancellationToken.None);
        _authWatcher = _moduleServiceProvider.GetRequiredService<TwitchAuthFileWatcher>();
        _authWatcher.Start();

        var channelManager = _moduleServiceProvider.GetRequiredService<ITwitchChannelManager>();
        await InitializeModuleServices(serviceProvider, config, channelManager);
        _authPollingService = new TwitchAuthPollingService(
            _localization,
            _moduleServiceProvider.GetRequiredService<IHttpClientFactory>(),
            _moduleServiceProvider.GetRequiredService<IOptions<TwitchConfiguration>>(),
            _twitchClient,
            channelManager,
            _db,
            _moduleServiceProvider.GetRequiredService<ILogger<TwitchAuthPollingService>>());
        await _authPollingService.StartAsync(CancellationToken.None);

        SubscribeEvents();
        _tokenManager.StateChanged += OnTokenStateChanged;
        if (_tokenManager.Current.BotCredential is not null)
            await ConnectAsync();

        IsInitialized = true;
    }

    public async Task ShutdownAsync()
    {
        if (!IsInitialized)
            return;

        UnsubscribeEvents();
        if (_tokenManager is not null)
            _tokenManager.StateChanged -= OnTokenStateChanged;
        if (_authWatcher is not null)
            await _authWatcher.DisposeAsync();
        if (_tokenRefreshService is not null)
            await _tokenRefreshService.StopAsync(CancellationToken.None);
        if (_authPollingService is not null)
            await _authPollingService.StopAsync(CancellationToken.None);
        await _twitchClient.DisconnectAsync();
        switch (_moduleServiceProvider)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
        
        IsInitialized = false;
        _logger.LogInformation("[tw] module shutdown complete");
    }

    // ><> init
    private static async Task<TwitchConfiguration> LoadConfigurationAsync(IServiceProvider sp)
    {
        var configurationService = sp.GetRequiredService<IConfigurationService>();
        return await configurationService.LoadConfigurationAsync<TwitchConfiguration>("Twitch") 
               ?? new TwitchConfiguration();
    }

    private void ResolveCoreDependencies(IServiceProvider sp, TwitchConfiguration config)
    {
        _logger = sp.GetRequiredService<ILogger<TwitchModule>>();
        _config = config;
        _db = sp.GetRequiredService<ICustomDataRepository>();
        _botCore = sp.GetRequiredService<IBotCore>();
        _dashboardBridge = sp.GetService<IDashboardBridge>();
        _localization = sp.GetService<ILocalizationService>();
        var localization = sp.GetRequiredService<ILocalizationService>();
        var pastebinService = sp.GetRequiredService<IPasteBinService>();
        _messageRender = new TwitchMessageRender(pastebinService, localization);
    }

    private static IServiceProvider CreateModuleServiceProvider(IServiceProvider host, TwitchConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddSingleton(host.GetRequiredService<IBotCore>());
        services.AddSingleton(host.GetRequiredService<ICustomDataRepository>());
        services.AddSingleton(host.GetRequiredService<ILocalizationService>());
        services.AddSingleton(host.GetRequiredService<IPasteBinService>());
        services.AddSingleton(host.GetRequiredService<IConfigurationService>());
        services.AddSingleton(host.GetRequiredService<IAppDataPathProvider>());
        services.AddSingleton(host.GetRequiredService<ILoggerFactory>());
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        if (host.GetService<IDashboardBridge>() is { } dashboard)
            services.AddSingleton(dashboard);
        if (host.GetService<ResiliencePipelineProvider<string>>() is { } pipelines)
            services.AddSingleton(pipelines);
        services.AddSingleton<ITwitchChannelManager, TwitchChannelManager>();
        services.AddTwitchChatTransports(options => CopyConfiguration(config, options));
        return services.BuildServiceProvider();
    }

    private static void CopyConfiguration(TwitchConfiguration source, TwitchConfiguration target)
    {
        target.BotUsername = source.BotUsername;
        target.BotUserId = source.BotUserId;
        target.Channel = source.Channel;
        target.ClientId = source.ClientId;
        target.ClientSecret = source.ClientSecret;
        target.RedirectUri = source.RedirectUri;
        target.AuthApiBaseUrl = source.AuthApiBaseUrl;
        target.BotApiToken = source.BotApiToken;
        target.IsEnabled = source.IsEnabled;
        target.CommandPrefix = source.CommandPrefix;
        target.ReplyMode = source.ReplyMode;
    }

    private async Task InitializeModuleServices(IServiceProvider sp, TwitchConfiguration config, ITwitchChannelManager channelManager)
    {
        var options = Options.Create(config);
        
        _twitchClient = await TwitchClient.CreateAsync(
            _moduleServiceProvider!.GetRequiredService<ResiliencePipelineProvider<string>>(),
            _moduleServiceProvider!.GetRequiredService<ILogger<TwitchClient>>(),
            channelManager.GetChannelsAsync().GetAwaiter().GetResult(),
            _db,
            _moduleServiceProvider!.GetRequiredService<ITwitchChatTransport>(),
            _tokenManager!
        );

        _broadcasterService = new TwitchBroadcasterService(
            _twitchClient,
            _db,
            _config,
            sp.GetRequiredService<ILogger<TwitchBroadcasterService>>(),
            channelManager);

        _commandFactories = new CommandFactories(this, _twitchClient, options, channelManager, sp);
    }

    // ><> events
    private void SubscribeEvents()
    {
        _twitchClient.OnMessageReceived += OnMessageReceived;
        _twitchClient.OnDisconnected += OnDisconnected;
    }

    private void UnsubscribeEvents()
    {
        _twitchClient.OnMessageReceived -= OnMessageReceived;
        _twitchClient.OnDisconnected -= OnDisconnected;
    }

    // ><> connection
    private async Task ConnectAsync()
    {
        try
        {
            if (_tokenManager?.Current.BotCredential is null)
                return;
            if (_twitchClient.IsConnected)
                return;
            await _twitchClient.ConnectAsync(_config.BotUsername, string.Empty, _config.ClientId);
            await _broadcasterService.LoadBroadcasterTokensAsync();
            
            await _twitchClient.JoinBotChannelAsync(_config.BotUsername);
            await SendBotConnectionMessageAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw] failed to init module connection");
            throw;
        }
    }

    private void OnTokenStateChanged(object? sender, TwitchTokenState state)
    {
        ExecuteSafeBackground(state.BotCredential is null ? DisconnectForMissingCredentialAsync() : ReconnectForCredentialAsync(),
            "[tw] unhandled exception while changing bot credential");
    }

    private async Task ReconnectForCredentialAsync()
    {
        if (_twitchClient.IsConnected)
            await _twitchClient.DisconnectAsync();
        await ConnectAsync();
    }

    private async Task DisconnectForMissingCredentialAsync()
    {
        await _twitchClient.DisconnectAsync();
    }

    // ><> event handlers
    private void OnDisconnected(object? sender, Events.OnDisconnectedArgs e) =>
        _logger.LogWarning("[tw] disconnected");

    private void OnMessageReceived(object? sender, Events.OnMessageReceivedArgs e) =>
        ExecuteSafeBackground(SafeHandleMessageAsync(e), "[tw] unhandled exception in message handler");

    private async Task SendBotConnectionMessageAsync()
    {
        if (_localization != null && _twitchClient.IsConnected)
        {
            var msg = await _localization.GetStringAsync("core.bot.connected", "EN_US");
            await _twitchClient.SendMessageAsync(_config.BotUsername, msg);
        }
    }

    private async Task SafeHandleMessageAsync(Events.OnMessageReceivedArgs e)
    {
        _dashboardBridge?.IncrementMessageCount();
        var chatMessage = e.ChatMessage;

        if (IsSelfMessage(chatMessage))
        {
            _logger.LogDebug("[tw] ignore self-message in #{Channel}", chatMessage.Channel);
            return;
        }

        await DispatchMessageToBotCoreAsync(chatMessage);
        await ProcessCommandIfAnyAsync(chatMessage);
    }

    // ><> message & command processing
    public async Task SendMessageAsync(string chatId, Message message, string? replyId = null, dynamic? data = null)
    {
        if (_messageRender == null)
            return;
        
        EnsureInitialized();
        var msg = await _messageRender.RenderTwitchMessageAsync(message);
        
        if (replyId == null)
            await _twitchClient.SendMessageAsync(chatId, msg, false);
        else
            await _twitchClient.SendReplyAsync(chatId, replyId, msg, false);
    }

    private async Task ProcessCommandIfAnyAsync(Models.ChatMessage chatMessage)
    {
        var prefix = await GetChannelPrefixAsync(chatMessage.ChannelId);
        if (!TryParseCommand(chatMessage.Message, prefix, out var commandName, out var arguments))
            return;

        var context = CreateCommandContext(chatMessage, commandName, arguments.ToList());
        var result = await _botCore.ProcessCommandAsync(context).ConfigureAwait(false);

        if (!result.SendResult)
        {
            _logger.LogInformation("[tw] not sent due to reply flag: '{result}'", result.Message?.RawText ?? "[empty]");
            return;
        }

        if (_twitchClient.IsConnected)
        {
            try
            {
                await SendResponseAsync(chatMessage, result.Message ?? new Message("Command executed"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[tw] failed to send command result back to #{Channel}", chatMessage.Channel);
            }
        }
    }

    private async Task SendResponseAsync(Models.ChatMessage triggeringMessage, Message responseMessage)
    {
        if (_messageRender == null)
            return;
        
        var textToSend = await _messageRender.RenderTwitchMessageAsync(responseMessage);
        if (string.IsNullOrWhiteSpace(textToSend))
            return;

        switch (_config.ReplyMode)
        {
            case TwitchReplyMode.Reply:
                await _twitchClient.SendReplyAsync(triggeringMessage.Channel, triggeringMessage.MessageId, textToSend);
                break;
            case TwitchReplyMode.Mention:
            default:
                await _twitchClient.SendMessageAsync(triggeringMessage.Channel, $"@{triggeringMessage.Username}, {textToSend}");
                break;
        }
    }

    private async Task DispatchMessageToBotCoreAsync(Models.ChatMessage chatMessage)
    {
        var extra = new TwitchMessageExtra
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
            new ChatMessage(
                Text: chatMessage.Message,
                ExtraData: extra,
                ReceivedAt: DateTime.UtcNow,
                PlatformUserId: chatMessage.UserId,
                PlatformUserName: chatMessage.Username,
                PlatformChatId: chatMessage.ChannelId,
                PlatformChatName: chatMessage.Channel
            )
        );
    }

    // ><> prefix & helpers
    private async ValueTask<string> GetChannelPrefixAsync(string channelId)
    {
        if (_prefixCache.TryGetValue(channelId, out var cached))
            return cached;

        try
        {
            var stored = await _db.GetDataAsync(SetPrefixCommand.GetPrefixKey(channelId));
            var prefix = !string.IsNullOrWhiteSpace(stored) ? stored : _config.CommandPrefix;
            _prefixCache[channelId] = prefix;
            return prefix;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[tw] failed to load prefix from Redis for #{ChannelId}, using default '{Default}'",
                channelId, _config.CommandPrefix);
            return _config.CommandPrefix;
        }
    }

    public void InvalidatePrefixCache(string channelId) => _prefixCache.TryRemove(channelId, out _);

    private bool IsSelfMessage(Models.ChatMessage msg) =>
        msg.UserId.Equals(_config.BotUserId, StringComparison.OrdinalIgnoreCase) ||
        msg.Username.Equals(_config.BotUsername, StringComparison.OrdinalIgnoreCase);

    private static bool TryParseCommand(string message, string prefix, out string commandName, out string[] arguments)
    {
        commandName = string.Empty;
        arguments = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(message) || !message.StartsWith(prefix))
            return false;

        var messageSpan = message.AsSpan(prefix.Length).TrimStart();
        var spaceIndex = messageSpan.IndexOf(' ');

        if (spaceIndex == -1)
        {
            commandName = messageSpan.ToString();
        }
        else
        {
            commandName = messageSpan[..spaceIndex].ToString();
            var argsString = messageSpan[(spaceIndex + 1)..].TrimStart().ToString();
            arguments = argsString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        return !string.IsNullOrWhiteSpace(commandName);
    }

    private CommandContext CreateCommandContext(Models.ChatMessage msg, string commandName, List<string> arguments, CancellationToken cancellationToken = default)
    {
        var chatMessage = new ChatMessage( 
            DateTime.UtcNow,
            msg.Message,
            msg.UserId,
            msg.Username,
            msg.ChannelId,
            msg.Channel,
            msg);

        var permissions = new HashSet<PlatformPermission>();
        if (msg.IsModerator)
            permissions.UnionWith([
                PlatformPermission.Moderator,
                PlatformPermission.CanBanUser,
                PlatformPermission.CanUnbanUser,
                PlatformPermission.CanDeleteOtherMessages,
                PlatformPermission.CanDeleteOwnMessages,
                PlatformPermission.CanUseBotCommands
            ]);
        if (msg.IsBroadcaster)
            permissions.UnionWith([
                PlatformPermission.Moderator,
                PlatformPermission.Owner,
                PlatformPermission.CanBanUser,
                PlatformPermission.CanUnbanUser,
                PlatformPermission.CanDeleteOtherMessages,
                PlatformPermission.CanDeleteOwnMessages,
                PlatformPermission.CanUseBotCommands,
                PlatformPermission.CanAddModerators,
                PlatformPermission.CanRemoveModerators,
                PlatformPermission.CanEditChatData
            ]);
        if (msg.IsBot)
            permissions.UnionWith([
                PlatformPermission.Moderator,
                PlatformPermission.CanBanUser,
                PlatformPermission.CanUnbanUser,
                PlatformPermission.CanDeleteOtherMessages,
                PlatformPermission.CanDeleteOwnMessages,
                PlatformPermission.CanUseBotCommands
            ]);
        if (msg.IsSubscriber)
            permissions.UnionWith([
                PlatformPermission.Vip
            ]);
        if (msg.IsVip)
            permissions.UnionWith([
                PlatformPermission.Vip
            ]);
        if (msg.Badges.Any(b => b.Key == "lead_moderator"))
            permissions.UnionWith([
                PlatformPermission.Moderator,
                PlatformPermission.CanBanUser,
                PlatformPermission.CanUnbanUser,
                PlatformPermission.CanDeleteOtherMessages,
                PlatformPermission.CanDeleteOwnMessages,
                PlatformPermission.CanUseBotCommands,
                PlatformPermission.CanAddModerators,
                PlatformPermission.CanRemoveModerators
            ]);
        
        return new CommandContext(
            commandName,
            ModuleId,
            arguments,
            new TwitchUser(msg.Username, msg.UserId, permissions),
            new TwitchChat(msg.Channel, msg.ChannelId),
            permissions,
            chatMessage,
            cancellationToken
        );
    }
    
    private void ExecuteSafeBackground(Task task, string errorMessage)
    {
        _ = task.ContinueWith(
            t => _logger.LogError(t.Exception, errorMessage),
            TaskContinuationOptions.OnlyOnFaulted
        );
    }

    private void EnsureInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("module is not initialized");
    }
}

// ><> test