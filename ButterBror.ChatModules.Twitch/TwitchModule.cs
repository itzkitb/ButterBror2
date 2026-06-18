using ButterBror.ChatModule;
using ButterBror.ChatModules.Twitch.Commands;
using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.ChatModules.Twitch.Services;
using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using ButterBror.Data;
using ButterBror.Domain.Chat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Registry;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using TwitchLib.Client.Events;

namespace ButterBror.ChatModules.Twitch;

public class TwitchModule : IChatModule
{
    public string ModuleId => "sillyapps:twitch";
    public Version Version => new(1, 0, 2);

    private Func<ICommand> _joinCommandFactory = null!;
    private Func<ICommand> _partCommandFactory = null!;
    private Func<ICommand> _setPrefixCommandFactory = null!;
    private Func<ICommand> _authCommandFactory = null!;
    private Func<ICommand> _addChannelCommandFactory = null!;
    private Func<ICommand> _deleteChannelCommandFactory = null!;
    private Func<ICommand> _channelSettingsCommandFactory = null!;

    private List<ModuleCommandExport> _commands = null!;
    public IReadOnlyList<ModuleCommandExport> ExportedCommands => _commands;

    private TwitchClient? _twitchClient = null!;
    private IBotCore? _botCore = null!;
    private ILogger<TwitchModule> _logger = null!;
    private TwitchConfiguration _config = null!;
    private ICustomDataRepository _db = null!;
    private IDashboardBridge? _dashboardBridge;
    private readonly ConcurrentDictionary<string, string> _prefixCache = new(StringComparer.Ordinal);
    
    public async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        var appDataPathProvider = serviceProvider.GetRequiredService<IAppDataPathProvider>();
        var configService = new TwitchConfigurationService(appDataPathProvider);
        var config = configService.LoadConfiguration();
        var options = Options.Create(config);
        _config = options.Value;
        _db = serviceProvider.GetRequiredService<ICustomDataRepository>();
        _logger = serviceProvider.GetRequiredService<ILogger<TwitchModule>>();
        _dashboardBridge = serviceProvider.GetService<IDashboardBridge>();
        _botCore = serviceProvider.GetService<IBotCore>();
        
        var ircChannelsString = _db.GetDataAsync("twitch:channels").GetAwaiter().GetResult() ?? "[]";
        var ircChannels = JsonSerializer.Deserialize<List<string>>(ircChannelsString) ?? [];

        _twitchClient = new TwitchClient(
            options,
            serviceProvider.GetRequiredService<ResiliencePipelineProvider<string>>(),
            serviceProvider.GetRequiredService<ILogger<TwitchClient>>(),
            ircChannels,
            _db
        );

        // S0: Updating factories
        _joinCommandFactory = () => new JoinChannelCommand(_twitchClient);
        _partCommandFactory = () => new PartChannelCommand(_twitchClient);
        _setPrefixCommandFactory = () => new SetPrefixCommand(this);
        _authCommandFactory = () => new AuthCommand(options);
        _addChannelCommandFactory = () => new AddChannelCommand(_twitchClient);
        _deleteChannelCommandFactory = () => new DeleteChannelCommand(_twitchClient);
        _channelSettingsCommandFactory = () => new ChannelSettingsCommand(_twitchClient);
        
        _commands = new List<ModuleCommandExport>
        {
            new("join", _joinCommandFactory, new JoinChannelCommandMetadata()),
            new("part", _partCommandFactory, new PartChannelCommandMetadata()),
            new("setprefix", _setPrefixCommandFactory, new SetPrefixCommandMetadata()),
            new("auth", _authCommandFactory, new AuthCommandMetadata()),
            new("addchannel", _addChannelCommandFactory, new AddChannelCommandMetadata()),
            new("deletechannel", _deleteChannelCommandFactory, new DeleteChannelCommandMetadata()),
            new("twitchset", _channelSettingsCommandFactory, new ChannelSettingsCommandMetadata())
        };
        
        // S1: Main events
        _twitchClient.OnMessageReceived += OnMessageReceived;
        _twitchClient.OnConnected += OnConnected;
        _twitchClient.OnDisconnected += OnDisconnected;
        _twitchClient.OnBroadcasterAuthReceived += OnBroadcasterAuthReceived;

        // S2: Subscribing to additional features (simplified logging)
        _twitchClient.OnNewSubscriber += (s, e) => _logger.LogInformation("[TW] New sub in #{Channel}: {User} ({Plan})", e.Channel, e.Username, e.SubscriptionPlan);
        _twitchClient.OnGiftedSubscription += (s, e) => _logger.LogInformation("[TW] Gifted sub in #{Channel}: {Gifter} -> {Recipient}", e.Channel, e.GifterUsername, e.RecipientUsername);
        _twitchClient.OnRaidNotification += (s, e) => _logger.LogInformation("[TW] Raid in #{Channel}: {Raider} ({Viewers} viewers)", e.Channel, e.RaiderUsername, e.ViewerCount);
        _twitchClient.OnBitsReceived += (s, e) => _logger.LogInformation("[TW] Bits in #{Channel}: {User} ({Bits} bits)", e.Channel, e.Username, e.Bits);

        if (!_config.IsEnabled)
        {
            _logger.LogWarning("[TW] Module is disabled in configuration");
            return;
        }

        if (string.IsNullOrWhiteSpace(_config.OauthToken))
        {
            _logger.LogError("[TW] OAuth token is missing. Module will not start!");
            return;
        }

        try
        {
            // S3: Connect to Twitch (WebSocket + IRC), but DON'T join channels yet
            await _twitchClient.ConnectAsync(_config.BotUsername, _config.OauthToken, _config.ClientId);

            // S4: Shit
            await LoadBroadcasterTokensAsync();

            // S5: Now join all configured channels
            await JoinConfiguredChannelsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TW] Failed to initialize module");
            throw;
        }
    }

    public async Task SendMessageAsync(string channel, string message) => await _twitchClient.SendMessageAsync(channel, message);
    
    private async Task JoinConfiguredChannelsAsync()
    {
        if (_twitchClient == null)
            throw new Exception("Twitch client not initialized");
        
        if (!string.IsNullOrWhiteSpace(_config.Channel))
        {
            await _twitchClient.JoinChannelAsync(_config.Channel);
        }
        else
        {
            await _twitchClient.JoinChannelAsync(_config.BotUsername);
        }
    }
    private void OnConnected(object? sender, OnConnectedEventArgs e)
    {
        _ = SafeHandleConnectAsync(e).ContinueWith(
            t => _logger.LogError(t.Exception, "[TW] Unhandled exception in connect handler"),
            TaskContinuationOptions.OnlyOnFaulted
        );
    }

    private async Task SafeHandleConnectAsync(OnConnectedEventArgs e)
    {
        if (_twitchClient == null)
            throw new Exception("Twitch client not initialized");
        
        if (_twitchClient.IsConnected && !string.IsNullOrWhiteSpace(_config.Channel))
        {
            await _twitchClient.SendMessageAsync(_config.Channel, "Bot connected successfully!");
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
        if (_twitchClient == null)
            throw new Exception("Twitch client not initialized");
        
        _dashboardBridge?.IncrementMessageCount();
        var chatMessage = e.ChatMessage;
        var isSelf = chatMessage.UserId.Equals(_config.BotUserId, StringComparison.OrdinalIgnoreCase) ||
                      chatMessage.Username.Equals(_config.BotUsername, StringComparison.OrdinalIgnoreCase);

        if (isSelf)
        {
            _logger.LogDebug("[TW] Ignoring self-message in #{Channel}", chatMessage.Channel);
            return;
        }
        
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
        if (TryParseCommand(chatMessage.Message, prefix, out var commandName, out var arguments))
        {
            var context = CreateCommandContext(chatMessage, commandName, arguments);
            var result = await _botCore.ProcessCommandAsync(context).ConfigureAwait(false);

            if (!result.SendResult)
            {
                _logger.LogInformation("[TW] Not sent due to reply flag: {result}", result.Message ?? "[]");
                return;
            }

            if (_twitchClient.IsConnected)
            {
                try
                {
                    await SendResponseAsync(chatMessage, result.Message ?? "Command executed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TW] Failed to send command result back to #{Channel}", chatMessage.Channel);
                }
            }
        }
    }

    private async Task SendResponseAsync(ChatMessage triggeringMessage, string responseText)
    {
        if (_twitchClient == null)
            throw new Exception("Twitch client not initialized");
        
        switch (_config.ReplyMode)
        {
            case TwitchReplyMode.Reply:
                await _twitchClient.SendReplyAsync(triggeringMessage.Channel, triggeringMessage.MessageId, responseText);
                break;
            case TwitchReplyMode.Mention:
            default:
                var mentionText = $"@{triggeringMessage.Username}, {responseText}";
                await _twitchClient.SendMessageAsync(triggeringMessage.Channel, mentionText);
                break;
        }
    }

    private bool TryParseCommand(string message, string prefix, out string commandName, out string[] arguments)
    {
        commandName = string.Empty;
        arguments = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(message) || !message.StartsWith(prefix)) return false;

        var messageSpan = message.AsSpan(prefix.Length).TrimStart();
        var spaceIndex = messageSpan.IndexOf(' ');

        if (spaceIndex == -1)
        {
            commandName = messageSpan.ToString();
            arguments = Array.Empty<string>();
        }
        else
        {
            commandName = messageSpan[..spaceIndex].ToString();
            var argsString = messageSpan[(spaceIndex + 1)..].TrimStart().ToString();
            arguments = argsString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        return !string.IsNullOrWhiteSpace(commandName);
    }

    private async ValueTask<string> GetChannelPrefixAsync(string channelId)
    {
        if (_prefixCache.TryGetValue(channelId, out var cached)) return cached;

        try
        {
            var stored = await _db.GetDataAsync(SetPrefixCommand.GetPrefixKey(channelId));
            var prefix = !string.IsNullOrWhiteSpace(stored) ? stored : _config.CommandPrefix;
            _prefixCache[channelId] = prefix;
            return prefix;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TW] Failed to load prefix from Redis for #{ChannelId}, using default '{Default}'", channelId, _config.CommandPrefix);
            return _config.CommandPrefix;
        }
    }

    public void InvalidatePrefixCache(string channelId) => _prefixCache.TryRemove(channelId, out _);

    private ICommandContext CreateCommandContext(ChatMessage chatMessage, string commandName, string[] arguments)
    {
        return new TwitchCommandContext(
            commandName,
            arguments,
            new TwitchUser(chatMessage.Username, chatMessage.UserId, chatMessage.IsModerator, chatMessage.IsBroadcaster, chatMessage.IsBot),
            new TwitchChannel(chatMessage.Channel, chatMessage.ChannelId),
            DateTime.UtcNow
        );
    }

    public async Task ShutdownAsync()
    {
        if (_twitchClient == null)
            return;
        
        _twitchClient.OnMessageReceived -= OnMessageReceived;
        _twitchClient.OnConnected -= OnConnected;
        _twitchClient.OnDisconnected -= OnDisconnected;
        _twitchClient.OnBroadcasterAuthReceived -= OnBroadcasterAuthReceived;
        await _twitchClient.DisconnectAsync();
        _logger.LogInformation("[TW] Module shutdown complete");
    }

    private async Task LoadBroadcasterTokensAsync()
    {
        try
        {
            if (_twitchClient == null)
                throw new Exception("Twitch client not initialized");
            
            Stopwatch timer = Stopwatch.StartNew();
            var allChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(_config.Channel)) allChannels.Add(_config.Channel);

            var ircJson = await _db.GetDataAsync("twitch:irc_channels") ?? "[]";
            var ircChannels = JsonSerializer.Deserialize<List<string>>(ircJson) ?? new();
            foreach (var ch in ircChannels) allChannels.Add(ch);
        
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 10 
            };

            await Parallel.ForEachAsync(allChannels, parallelOptions, async (channel, cancellationToken) =>
            {
                try
                {
                    var channelId = await _twitchClient.GetChannelIdAsync(channel);
                    if (string.IsNullOrWhiteSpace(channelId)) return;

                    var tokenKey = $"twitch:broadcaster_token:{channelId}";
                    var token = await _db.GetDataAsync(tokenKey);

                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        _twitchClient.SetBroadcasterToken(channelId, token);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[TW] Failed to load broadcaster token for #{Channel}", channel);
                }
            });
            
            timer.Stop();
            _logger.LogInformation("[TW] Loaded broadcaster token for {Channels} channels in {Time} ms", allChannels.Count, timer.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TW] Failed to load broadcaster tokens from Redis");
        }
    }

    private void OnBroadcasterAuthReceived(object? sender, BroadcasterAuthReceivedArgs e)
    {
        _ = SafeHandleBroadcasterAuthAsync(e).ContinueWith(
            t => _logger.LogError(t.Exception, "[TW] Unhandled exception in broadcaster auth handler"),
            TaskContinuationOptions.OnlyOnFaulted
        );
    }

    private async Task SafeHandleBroadcasterAuthAsync(BroadcasterAuthReceivedArgs e)
    {
        try
        {
            if (_twitchClient == null)
                throw new Exception("Twitch client not initialized");
            
            var channelId = await _twitchClient.GetChannelIdAsync(e.Channel);
            if (string.IsNullOrWhiteSpace(channelId))
            {
                _logger.LogWarning("[TW] Channel {Channel} not found", e.Channel);
                return;
            }

            var isValid = await _twitchClient.ValidateBroadcasterTokenAsync(e.Token);
            if (!isValid)
            {
                _logger.LogWarning("[TW] Invalid broadcaster token from {User} for #{Channel}", e.Username, e.Channel);
                await _twitchClient.SendMessageAsync(e.Channel, "❌ | Failed to authorize. The token is invalid or expired");
                return;
            }

            var tokenKey = $"twitch:broadcaster_token:{channelId}";
            await _db.SetDataAsync(tokenKey, e.Token);
            _twitchClient.SetBroadcasterToken(channelId, e.Token);
            await _twitchClient.AddChannelAsync(e.Channel);
            await _twitchClient.SendMessageAsync(e.Channel, "✅ | Successfully authorized, hi!");

            _logger.LogInformation("[TW] Successfully authorized #{Channel}", e.Channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TW] Error processing broadcaster auth from {User}", e.Username);
        }
    }
}