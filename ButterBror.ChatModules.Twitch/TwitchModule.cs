using System.Collections.Concurrent;
using ButterBror.ChatModules.Twitch.Commands;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.ChatModules.Twitch.Services;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Messaging;
using ButterBror.Core.Modules;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Enums;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Data;
using ButterBror.Domain.Chat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Registry;
using TwitchLib.Client.Events;
using ChatMessage = ButterBror.Domain.Chat.ChatMessage;

namespace ButterBror.ChatModules.Twitch;

public class TwitchModule : IChatModule
{
    // ><> Metadata
    public string ModuleId => "sillyapps:twitch";
    public Version Version { get; } = new(1, 4, 0);
    public List<ChatModuleFlags> Flags { get; } = [ChatModuleFlags.CanSendMessages];

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> DefaultTranslations =>
        Services.Localization.DefaultTranslations;

    // ><> State & Dependencies
    private readonly ConcurrentDictionary<string, string> _prefixCache = new(StringComparer.Ordinal);
    private TwitchMessageRender? _messageRender;

    private ILogger<TwitchModule> _logger = null!;
    private IBotCore _botCore = null!;
    private TwitchConfiguration _config = null!;
    private ICustomDataRepository _db = null!;
    private IDashboardBridge? _dashboardBridge;
    private ILocalizationService? _localization;

    private TwitchClient _twitchClient = null!;
    private TwitchBroadcasterService _broadcasterService = null!;
    private CommandFactories _commandFactories = null!;

    public bool IsInitialized { get; private set; }
    public bool IsConnected => _twitchClient.IsConnected;
    public IReadOnlyList<ModuleCommandExport> ExportedCommands => _commandFactories.Export;

    // ><> Lifecycle
    public async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        if (IsInitialized) return;

        var config = await LoadConfigurationAsync(serviceProvider);
        if (!config.IsEnabled || string.IsNullOrWhiteSpace(config.OauthToken))
        {
            var logger = serviceProvider.GetRequiredService<ILogger<TwitchModule>>();
            logger.LogWarning("[TW] Module is disabled or OAuth token is missing. Module will not start!");
            return;
        }

        ResolveCoreDependencies(serviceProvider, config);
        
        var channelManager = await RegisterAndGetChannelManagerAsync(serviceProvider);
        InitializeModuleServices(serviceProvider, config, channelManager);

        SubscribeEvents();
        await ConnectAsync();

        IsInitialized = true;
    }

    public async Task ShutdownAsync()
    {
        if (!IsInitialized)
            return;

        UnsubscribeEvents();
        await _twitchClient.DisconnectAsync();
        
        IsInitialized = false;
        _logger.LogInformation("[TW] Module shutdown complete");
    }

    // ><> Initialization
    private async Task<TwitchConfiguration> LoadConfigurationAsync(IServiceProvider sp)
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
        _messageRender = new(pastebinService, localization);
    }

    private static async Task<ITwitchChannelManager> RegisterAndGetChannelManagerAsync(IServiceProvider sp)
    {
        var dynamicSp = sp.GetRequiredService<IDynamicServiceProvider>();
        dynamicSp.AddSingleton<ITwitchChannelManager, TwitchChannelManager>();
        return dynamicSp.GetRequiredService<ITwitchChannelManager>();
    }

    private void InitializeModuleServices(IServiceProvider sp, TwitchConfiguration config, ITwitchChannelManager channelManager)
    {
        var options = Options.Create(config);
        
        _twitchClient = new TwitchClient(
            options,
            sp.GetRequiredService<ResiliencePipelineProvider<string>>(),
            sp.GetRequiredService<ILogger<TwitchClient>>(),
            channelManager.GetChannelsAsync().GetAwaiter().GetResult(),
            _db
        );

        _broadcasterService = new TwitchBroadcasterService(
            _twitchClient,
            _db,
            _config,
            sp.GetRequiredService<ILogger<TwitchBroadcasterService>>(),
            channelManager);

        _commandFactories = new CommandFactories(this, _twitchClient, options, channelManager, sp);
    }

    // ><> Events
    private void SubscribeEvents()
    {
        _twitchClient.OnMessageReceived += OnMessageReceived;
        _twitchClient.OnConnected += OnConnected;
        _twitchClient.OnDisconnected += OnDisconnected;
        _twitchClient.OnBroadcasterAuthReceived += _broadcasterService.OnBroadcasterAuthReceived;

        _twitchClient.OnNewSubscriber += (_, e) => _logger.LogInformation("[TW] New sub in #{Channel}: {User} ({Plan})", e.Channel, e.Username, e.SubscriptionPlan);
        _twitchClient.OnGiftedSubscription += (_, e) => _logger.LogInformation("[TW] Gifted sub in #{Channel}: {Gifter} -> {Recipient}", e.Channel, e.GifterUsername, e.RecipientUsername);
        _twitchClient.OnRaidNotification += (_, e) => _logger.LogInformation("[TW] Raid in #{Channel}: {Raider} ({Viewers} viewers)", e.Channel, e.RaiderUsername, e.ViewerCount);
        _twitchClient.OnBitsReceived += (_, e) => _logger.LogInformation("[TW] Bits in #{Channel}: {User} ({Bits} bits)", e.Channel, e.Username, e.Bits);
    }

    private void UnsubscribeEvents()
    {
        _twitchClient.OnMessageReceived -= OnMessageReceived;
        _twitchClient.OnConnected -= OnConnected;
        _twitchClient.OnDisconnected -= OnDisconnected;
        _twitchClient.OnBroadcasterAuthReceived -= _broadcasterService.OnBroadcasterAuthReceived;
    }

    // ><> Connection
    private async Task ConnectAsync()
    {
        try
        {
            await _twitchClient.ConnectAsync(_config.BotUsername, _config.OauthToken, _config.ClientId);
            await _broadcasterService.LoadBroadcasterTokensAsync();
            
            var targetChannel = !string.IsNullOrWhiteSpace(_config.Channel) ? _config.Channel : _config.BotUsername;
            await _twitchClient.JoinChannelAsync(targetChannel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TW] Failed to initialize module connection");
            throw;
        }
    }

    // ><> Event Handlers
    private void OnConnected(object? sender, OnConnectedEventArgs e) =>
        ExecuteSafeBackground(SafeHandleConnectAsync(e), "[TW] Unhandled exception in connect handler");

    private void OnDisconnected(object? sender, OnDisconnectedArgs e) =>
        _logger.LogWarning("[TW] Disconnected");

    private void OnMessageReceived(object? sender, Events.OnMessageReceivedArgs e) =>
        ExecuteSafeBackground(SafeHandleMessageAsync(e), "[TW] Unhandled exception in message handler");

    private async Task SafeHandleConnectAsync(OnConnectedEventArgs _)
    {
        if (_localization != null && _twitchClient.IsConnected && !string.IsNullOrWhiteSpace(_config.Channel))
        {
            var msg = await _localization.GetStringAsync("core.bot.connected", "EN_US");
            await _twitchClient.SendMessageAsync(_config.Channel, msg);
        }
    }

    private async Task SafeHandleMessageAsync(Events.OnMessageReceivedArgs e)
    {
        _dashboardBridge?.IncrementMessageCount();
        var chatMessage = e.ChatMessage;

        if (IsSelfMessage(chatMessage))
        {
            _logger.LogDebug("[TW] Ignoring self-message in #{Channel}", chatMessage.Channel);
            return;
        }

        await DispatchMessageToBotCoreAsync(chatMessage);
        await ProcessCommandIfAnyAsync(chatMessage);
    }

    // ><> Message & Command Processing
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
            _logger.LogInformation("[TW] Not sent due to reply flag: {result}", result.Message?.RawText ?? "[]");
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
                _logger.LogError(ex, "[TW] Failed to send command result back to #{Channel}", chatMessage.Channel);
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

    // ><> Prefix & Helpers
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
            _logger.LogWarning(ex, "[TW] Failed to load prefix from Redis for #{ChannelId}, using default '{Default}'",
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
        
        return new CommandContext(
            commandName,
            ModuleId,
            arguments,
            new TwitchUser(msg.Username, msg.UserId, msg.IsModerator, msg.IsBroadcaster, msg.IsBot),
            new TwitchChannel(msg.Channel, msg.ChannelId),
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
            throw new InvalidOperationException("[TW] Module is not initialized.");
    }
}