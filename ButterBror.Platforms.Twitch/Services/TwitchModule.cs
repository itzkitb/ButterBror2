using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using ButterBror.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwitchLib.Client.Events;

namespace ButterBror.Platforms.Twitch.Services;

public class TwitchModule : IPlatformModule
{
    public string PlatformName => "Twitch";
    private readonly ITwitchClient _twitchClient;
    private IBotCore _botCore;
    private readonly ICommandParser _commandParser;
    private readonly ILogger<TwitchModule> _logger;
    private readonly TwitchConfiguration _config;

    public TwitchModule(
        ITwitchClient twitchClient,
        ICommandParser commandParser,
        IOptions<TwitchConfiguration> config,
        ILogger<TwitchModule> logger)
    {
        _twitchClient = twitchClient;
        _commandParser = commandParser;
        _config = config.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(IBotCore core)
    {
        _botCore = core;
        // Подписка на основные события
        _twitchClient.OnMessageReceived += OnMessageReceived;
        _twitchClient.OnConnected += OnConnected;
        _twitchClient.OnDisconnected += OnDisconnected;

        // Подписка на дополнительные события для расширенной функциональности
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
                await _twitchClient.ConnectAsync(_config.BotUsername, _config.OauthToken, _config.Channel);
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
        _logger.LogInformation("Connected to Twitch: {Username}", e.BotUsername);
        // Отправка приветственного сообщения
        if (_twitchClient is TwitchLibClient libClient && libClient.IsConnected)
        {
            try
            {
                await libClient.SendMessageAsync(_config.Channel, $"Bot connected successfully!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send welcome message");
            }
        }
    }

    private async void OnDisconnected(object? sender, OnDisconnectedArgs e)
    {
        _logger.LogWarning("Disconnected from Twitch");
    }

    private async void OnMessageReceived(object? sender, OnMessageReceivedArgs e)
    {
        try
        {
            // Парсим команду напрямую из сообщения
            if (TryParseCommand(e.ChatMessage.Message, out var commandName, out var arguments))
            {
                var context = CreateCommandContext(e.ChatMessage, commandName, arguments);
                await _botCore.ProcessCommandAsync(context);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Twitch message");
        }
    }

    // Восстанавливаем метод TryParseCommand в модуле
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

    // Обработка новых событий (остается без изменений)
    private async void OnNewSubscriber(object? sender, OnNewSubscriberArgs e)
    {
        if (_twitchClient is TwitchLibClient libClient && libClient.IsConnected)
        {
            try
            {
                string message = $"{e.Username} just subscribed with {e.SubscriptionPlan}! Thank you for {e.Months} months!";
                await libClient.SendMessageAsync(_config.Channel, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send subscriber message");
            }
        }
    }

    private async void OnGiftedSubscription(object? sender, OnGiftedSubscriptionArgs e)
    {
        if (_twitchClient is TwitchLibClient libClient && libClient.IsConnected)
        {
            try
            {
                string message = $"{e.GifterUsername} gifted a subscription to {e.RecipientUsername}!";
                await libClient.SendMessageAsync(_config.Channel, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send gifted subscription message");
            }
        }
    }

    private async void OnRaidNotification(object? sender, OnRaidNotificationArgs e)
    {
        if (_twitchClient is TwitchLibClient libClient && libClient.IsConnected)
        {
            try
            {
                string message = $"raidW 🚨 RAID from {e.RaiderUsername} with {e.ViewerCount} viewers! Welcome everyone!";
                await libClient.SendMessageAsync(_config.Channel, message);
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
                string message = $"{e.Username} just cheered {e.Bits} bits! Thank you so much!";
                await libClient.SendMessageAsync(_config.Channel, message);
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
        // Handle direct messages or other Twitch-specific messages
        _logger.LogInformation("Received direct message from {Sender} on Twitch", message.Sender.DisplayName);
        if (_twitchClient is TwitchLibClient libClient && libClient.IsConnected)
        {
            try
            {
                await libClient.SendWhisperAsync(message.Sender.DisplayName, "Hello! I received your message.");
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