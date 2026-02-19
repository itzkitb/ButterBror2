using ButterBror.ChatModules.Abstractions;
using ButterBror.ChatModules.Twitch.Commands;
using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using ButterBror.Core.Models.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.Registry;
using TwitchLib.Client.Events;

namespace ButterBror.ChatModules.Twitch.Services;

public class TwitchModule : IChatModuleWithServices
{
    public string PlatformName => "sillyapps:twitch";

    // Factories
    private Func<Core.Interfaces.ICommand> _joinCommandFactory = null!;
    private Func<Core.Interfaces.ICommand> _partCommandFactory = null!;

    public IReadOnlyList<ModuleCommandExport> ExportedCommands => new List<ModuleCommandExport>
    {
        new ModuleCommandExport("join", _joinCommandFactory, new JoinChannelCommandMetadata()),
        new ModuleCommandExport("part", _partCommandFactory, new PartChannelCommandMetadata())
    };

    private ITwitchClient _twitchClient = null!;
    private IBotCore _botCore = null!;
    private ILogger<TwitchModule> _logger = null!;
    private TwitchConfiguration _config = null!;

    public TwitchModule()
    {
        // Пустой конструктор для загрузки из DLL
    }

    public void InitializeWithServices(IServiceProvider serviceProvider)
    {
        var appDataPathProvider = serviceProvider.GetRequiredService<IAppDataPathProvider>();
        var configService = new TwitchConfigurationService(appDataPathProvider);
        var config = configService.LoadConfiguration();
        var options = Options.Create(config);
        _config = options.Value;
        
        _logger = serviceProvider.GetRequiredService<ILogger<TwitchModule>>();
        
        _twitchClient = new TwitchLibClient(
            options,
            serviceProvider.GetRequiredService<ResiliencePipelineProvider<string>>(),
            serviceProvider.GetRequiredService<ILogger<TwitchLibClient>>()
        );
        
        // Updating factories with client capture
        _joinCommandFactory = () => new JoinChannelCommand(_twitchClient);
        _partCommandFactory = () => new PartChannelCommand(_twitchClient);
    }

    public async Task InitializeAsync(IBotCore core)
    {
        _botCore = core;
        // Main events
        _twitchClient.OnMessageReceived += OnMessageReceived;
        _twitchClient.OnConnected += OnConnected;
        _twitchClient.OnDisconnected += OnDisconnected;

        // Subscribing to additional features
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
                _logger.LogError("Twitch OAuth token is missing. Module will not start.");
                return;
            }

            try
            {
                await _twitchClient.ConnectAsync(_config.BotUsername, _config.OauthToken, _config.ClientId, _config.Channel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Twitch module");
                throw;
            }
        }
        else
        {
            _logger.LogWarning("Twitch module is disabled in configuration");
        }
    }

    private async void OnConnected(object? sender, OnConnectedEventArgs e)
    {
        _ = SafeHandleConnectAsync(e).ContinueWith(
            t => _logger.LogError(t.Exception, "Unhandled exception in connect handler"),
            TaskContinuationOptions.OnlyOnFaulted
        );
    }

    private async Task SafeHandleConnectAsync(OnConnectedEventArgs e)
    {
        _logger.LogInformation("Connected to Twitch: {Username}", e.BotUsername);
        if (_twitchClient is TwitchLibClient libClient && libClient.IsConnected)
        {
            await libClient.SendMessageAsync(_config.Channel, $"Bot connected successfully!");
        }
    }

    private async void OnDisconnected(object? sender, OnDisconnectedArgs e)
    {
        _logger.LogWarning("Disconnected from Twitch");
    }

    private void OnMessageReceived(object? sender, Events.OnMessageReceivedArgs e)
    {
        _ = SafeHandleMessageAsync(e).ContinueWith(
            t => _logger.LogError(t.Exception, "Unhandled exception in message handler"),
            TaskContinuationOptions.OnlyOnFaulted
        );
    }

    private async Task SafeHandleMessageAsync(Events.OnMessageReceivedArgs e)
    {
        if (TryParseCommand(e.ChatMessage.Message, out var commandName, out var arguments))
        {
            var context = CreateCommandContext(e.ChatMessage, commandName, arguments);
            var result = await _botCore.ProcessCommandAsync(context).ConfigureAwait(false);

            if (!result.SendResult)
            {
                _logger.LogInformation("The command has requested not to send the result: {result}", result.Message ?? "[]");
                return;
            }

            if (_twitchClient is TwitchLibClient libClient && libClient.IsConnected)
            {
                try
                {
                    await libClient.SendMessageAsync(e.ChatMessage.Channel, result.Message ?? "Command executed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send command result back to channel {Channel}", e.ChatMessage.Channel);
                }
            }
        }
    }

    private bool TryParseCommand(string message, out string commandName, out string[] arguments)
    {
        commandName = string.Empty;
        arguments = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(message) || !message.StartsWith('!'))
        {
            return false;
        }

        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        commandName = parts[0].TrimStart('!');
        arguments = parts.Skip(1).ToArray();

        return !string.IsNullOrWhiteSpace(commandName);
    }

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
                _logger.LogError(ex, "Failed to send subscriber message");
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
                _logger.LogError(ex, "Failed to send gifted subscription message");
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
                _logger.LogError(ex, "Failed to send raid notification message");
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
                _logger.LogError(ex, "Failed to send bits message");
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
            new TwitchChannel(chatMessage.Channel),
            DateTime.UtcNow
        );
    }

    public async Task HandleIncomingMessageAsync(IMessage message)
    {
        _logger.LogInformation("Received direct message from {Sender} on Twitch", message.Sender.DisplayName);
        if (_twitchClient is TwitchLibClient libClient && libClient.IsConnected)
        {
            try
            {
                // TODO: Add something here idk
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send whisper response");
            }
        }
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
        _logger.LogInformation("Twitch module shutdown complete");
    }
}
