using ButterBror.Core.Interfaces;
using ButterBror.Platforms.Twitch.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using System.Text.RegularExpressions;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Enums;
using TwitchLib.Client.Events;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using TwitchLib.EventSub.Websockets;

namespace ButterBror.Platforms.Twitch.Services;

public class TwitchLibClient : ITwitchClient, IDisposable
{
    private readonly TwitchClient _client;
    private readonly TwitchAPI _clientAPI;
    private readonly TwitchConfiguration _config;
    private readonly EventSubWebsocketClient _eventSubClient;
    private readonly ResiliencePipeline _twitchPipeline;
    private readonly ILogger<TwitchLibClient> _logger;
    private bool _isDisposed;

    private string _username;
    private string _id;

    public event EventHandler<OnMessageReceivedArgs>? OnMessageReceived;
    public event EventHandler<OnConnectedEventArgs>? OnConnected;
    public event EventHandler<OnDisconnectedArgs>? OnDisconnected;

    // Дополнительные события для расширенной функциональности
    public event EventHandler<OnUserJoinedArgs>? OnUserJoined;
    public event EventHandler<OnUserLeftArgs>? OnUserLeft;
    public event EventHandler<OnNewSubscriberArgs>? OnNewSubscriber;
    public event EventHandler<OnGiftedSubscriptionArgs>? OnGiftedSubscription;
    public event EventHandler<OnRaidNotificationArgs>? OnRaidNotification;
    public event EventHandler<OnBitsReceivedArgs>? OnBitsReceived;

    public TwitchLibClient(
        IOptions<TwitchConfiguration> config,
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<TwitchLibClient> logger)
    {
        _config = config.Value;
        _logger = logger;
        _twitchPipeline = pipelineProvider.GetPipeline("twitch");

        // Настройка клиента с параметрами для надежности
        var clientOptions = new ClientOptions();

        var websocketClient = new WebSocketClient(clientOptions);
        _client = new TwitchClient(websocketClient);
        _clientAPI = new TwitchAPI();
        _eventSubClient = new EventSubWebsocketClient();

        // Подписка на события TwitchLib
        _client.OnMessageReceived += OnTwitchMessageReceived;
        _client.OnConnected += OnTwitchConnected;
        _client.OnDisconnected += OnTwitchDisconnected;
        _client.OnConnectionError += OnTwitchConnectionError;
        _client.OnJoinedChannel += OnTwitchJoinedChannel;
        _client.OnLeftChannel += OnTwitchLeftChannel;
        _client.OnNewSubscriber += OnTwitchNewSubscriber;
        _client.OnGiftedSubscription += OnTwitchGiftedSubscription;
        _client.OnRaidNotification += OnTwitchRaidNotification;
        _client.OnBitsBadgeTier += OnTwitchBitsReceived;

        // Настройка обработчика команд
        _client.OnChatCommandReceived += OnTwitchChatCommandReceived;

        _logger.LogInformation("TwitchLib client initialized");
    }

    public async Task ConnectAsync(string username, string oauthToken, string clientId, string channel)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(TwitchLibClient));

        try
        {
            _logger.LogInformation("Attempting to connect to Twitch channel: {Channel}", channel);

            // Проверка конфигурации
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(oauthToken) || string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException("Twitch username and OAuth token/ClientId are required");
            }

            // Настройка credentials
            var credentials = new ConnectionCredentials(
                twitchUsername: username,
                twitchOAuth: oauthToken,
                disableUsernameCheck: true // Разрешаем любой юзернейм для ботов
            );

            _client.Initialize(credentials, channel);

            // Подключение с таймаутом
            var connectTask = _client.ConnectAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));

            if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
            {
                await _client.DisconnectAsync();
                throw new TimeoutException("Connection to Twitch timed out after 30 seconds");
            }

            await connectTask;

            _logger.LogInformation("Successfully connected to Twitch channel: {Channel}", channel);

            _ = InitializeAPI(clientId, oauthToken);
            _ = InitializeEventSub();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Twitch channel: {Channel}", channel);
            throw;
        }
    }

    private async Task InitializeAPI(string clientId, string oauthToken)
    {
        _clientAPI.Settings.ClientId = clientId;
        _clientAPI.Settings.AccessToken = oauthToken.Substring("oauth:".Length);

        _username = _client.TwitchUsername;
        _id = await _twitchPipeline.ExecuteAsync(async ct =>
            (await _clientAPI.Helix.Users.GetUsersAsync(logins: [_username])).Users[0].Id
        );
        _logger.LogInformation("Twitch API initialized");
    }

    private async Task InitializeEventSub()
    {
        _eventSubClient.WebsocketConnected += async (s, e) =>
        {
            // Subscribe to whispers for your user account
            await _clientAPI.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "user.whisper.message", "1",
                new Dictionary<string, string> { { "user_id", _id } },
                TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                _eventSubClient.SessionId
            );
        };

        // 2. Hook into the whisper event itself
        _eventSubClient.UserWhisperMessage += async (s, e) =>
        {
            var msg = e.Payload.Event;
            _logger.LogDebug($"Whisper from {msg.FromUserName}: {msg.Whisper.Text}");
            _ = SendWhisperAsync(msg.FromUserId, "Hi!");

            await Task.CompletedTask;
        };

        // 3. Connect to the Twitch EventSub server
        await _eventSubClient.ConnectAsync();
        _logger.LogInformation("Twitch EventSub initialized");
    }

    public async Task DisconnectAsync()
    {
        if (_isDisposed) return;

        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync();
                _logger.LogInformation("Disconnected from Twitch");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Twitch disconnection");
        }
    }

    public async Task SendMessageAsync(string channel, string message)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(TwitchLibClient));

        if (!_client.IsConnected)
            throw new InvalidOperationException("Not connected to Twitch");

        try
        {
            // Очистка сообщения от запрещенных символов
            var sanitizedMessage = SanitizeMessage(message);

            // Проверка длины сообщения
            if (sanitizedMessage.Length > 500)
            {
                sanitizedMessage = sanitizedMessage[..497] + "...";
            }

            await _client.SendMessageAsync(channel, sanitizedMessage);
            _logger.LogDebug("Sent message to channel {Channel}: {Message}", channel, sanitizedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to channel {Channel}", channel);
            throw;
        }
    }

    public async Task SendWhisperAsync(string recipientUserId, string message)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(TwitchLibClient));

        if (!_client.IsConnected)
            throw new InvalidOperationException("Not connected to Twitch");

        try
        {
            var sanitizedMessage = SanitizeMessage(message);

            await _twitchPipeline.ExecuteAsync(async ct =>
                await _clientAPI.Helix.Whispers.SendWhisperAsync(_id, recipientUserId, message, true)
            );
            _logger.LogDebug("Sent whisper to {Recipient}: {Message}", recipientUserId, sanitizedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send whisper to {Recipient}", recipientUserId);
            throw;
        }
    }

    public bool IsConnected => _client.IsConnected;

    private async Task OnTwitchMessageReceived(object? sender, TwitchLib.Client.Events.OnMessageReceivedArgs e)
    {
        try
        {
            // Конвертация в наш формат события
            var chatMessage = new ChatMessage
            {
                Username = e.ChatMessage.Username,
                UserId = e.ChatMessage.UserId,
                Message = e.ChatMessage.Message,
                Channel = e.ChatMessage.Channel,
                IsModerator = e.ChatMessage.UserDetail.IsModerator,
                IsBroadcaster = e.ChatMessage.IsBroadcaster,
                IsSubscriber = e.ChatMessage.UserDetail.IsSubscriber,
                IsVIP = e.ChatMessage.UserDetail.IsVip,
                Badges = e.ChatMessage.BadgeInfo,
                Color = e.ChatMessage.HexColor
            };

            OnMessageReceived?.Invoke(this, new OnMessageReceivedArgs { ChatMessage = chatMessage });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Twitch message received event");
        }
    }

    private async Task OnTwitchConnected(object? sender, OnConnectedEventArgs e)
    {
        try
        {
            _logger.LogInformation("Connected to Twitch IRC server");
            OnConnected?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Twitch connected event");
        }
    }

    private async Task OnTwitchDisconnected(object? sender, OnDisconnectedArgs e)
    {
        _logger.LogWarning("Disconnected from Twitch");
    }

    private async Task OnTwitchConnectionError(object? sender, OnConnectionErrorArgs e)
    {
        _logger.LogError("Twitch connection error: {Error}", e.Error);
    }

    private async Task OnTwitchJoinedChannel(object? sender, OnJoinedChannelArgs e)
    {
        _logger.LogInformation("Joined channel: {Channel}", e.Channel);
    }

    private async Task OnTwitchLeftChannel(object? sender, OnLeftChannelArgs e)
    {
        _logger.LogInformation("Left channel: {Channel}", e.Channel);
    }

    private async Task OnTwitchNewSubscriber(object? sender, TwitchLib.Client.Events.OnNewSubscriberArgs e)
    {
        try
        {
            var args = new OnNewSubscriberArgs
            {
                Channel = e.Channel,
                Username = e.Subscriber.Login,
                SubscriptionPlan = e.Subscriber.MsgParamSubPlanName,
                Months = e.Subscriber.MsgParamCumulativeMonths
            };
            OnNewSubscriber?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling new subscriber event");
        }
    }

    private async Task OnTwitchGiftedSubscription(object? sender, TwitchLib.Client.Events.OnGiftedSubscriptionArgs e)
    {
        try
        {
            var args = new OnGiftedSubscriptionArgs
            {
                Channel = e.Channel,
                GifterUsername = e.GiftedSubscription.Login,
                RecipientUsername = e.GiftedSubscription.MsgParamRecipientUserName,
                SubscriptionPlan = e.GiftedSubscription.MsgParamSubPlanName
            };
            OnGiftedSubscription?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling gifted subscription event");
        }
    }

    private async Task OnTwitchRaidNotification(object? sender, TwitchLib.Client.Events.OnRaidNotificationArgs e)
    {
        try
        {
            var args = new OnRaidNotificationArgs
            {
                Channel = e.Channel,
                RaiderUsername = e.RaidNotification.Login,
                ViewerCount = Int32.Parse(e.RaidNotification.MsgParamViewerCount)
            };
            OnRaidNotification?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling raid notification event");
        }
    }

    private async Task OnTwitchBitsReceived(object? sender, OnBitsBadgeTierArgs e)
    {
        try
        {
            var args = new OnBitsReceivedArgs
            {
                Channel = e.Channel,
                Username = e.BitsBadgeTier.Login,
                Bits = e.BitsBadgeTier.MsgParamThreshold, // wth with this lib
                Message = e.BitsBadgeTier.SystemMsg
            };
            OnBitsReceived?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling bits received event");
        }
    }

    private async Task OnTwitchChatCommandReceived(object? sender, OnChatCommandReceivedArgs e)
    {
        // Этот обработчик нужен для внутренней логики TwitchLib
        // Основная обработка команд будет в TwitchModule через OnMessageReceived
    }

    private string SanitizeMessage(string message)
    {
        // Удаляем запрещенные символы и избыточные пробелы
        return Regex.Replace(message, @"\s+", " ")
            .Replace("\r", "")
            .Replace("\n", " ")
            .Trim();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed) return;

        if (disposing)
        {
            try
            {
                _isDisposed = true;

                if (_client.IsConnected)
                {
                    _client.DisconnectAsync();
                }

                _client.OnMessageReceived -= OnTwitchMessageReceived;
                _client.OnConnected -= OnTwitchConnected;
                _client.OnDisconnected -= OnTwitchDisconnected;
                _client.OnConnectionError -= OnTwitchConnectionError;
                _client.OnJoinedChannel -= OnTwitchJoinedChannel;
                _client.OnLeftChannel -= OnTwitchLeftChannel;
                _client.OnNewSubscriber -= OnTwitchNewSubscriber;
                _client.OnGiftedSubscription -= OnTwitchGiftedSubscription;
                _client.OnRaidNotification -= OnTwitchRaidNotification;
                _client.OnBitsBadgeTier -= OnTwitchBitsReceived;
                _client.OnChatCommandReceived -= OnTwitchChatCommandReceived;

                _logger.LogInformation("TwitchLib client disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Twitch client disposal");
            }
        }

        _isDisposed = true;
    }

    ~TwitchLibClient()
    {
        Dispose(false);
    }
}

// Дополнительные аргументы событий для расширенной функциональности
public class OnUserJoinedArgs : EventArgs
{
    public string Channel { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

public class OnUserLeftArgs : EventArgs
{
    public string Channel { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

public class OnNewSubscriberArgs : EventArgs
{
    public string Channel { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string SubscriptionPlan { get; set; } = string.Empty;
    public int Months { get; set; }
}

public class OnGiftedSubscriptionArgs : EventArgs
{
    public string Channel { get; set; } = string.Empty;
    public string GifterUsername { get; set; } = string.Empty;
    public string RecipientUsername { get; set; } = string.Empty;
    public string SubscriptionPlan { get; set; } = string.Empty;
}

public class OnRaidNotificationArgs : EventArgs
{
    public string Channel { get; set; } = string.Empty;
    public string RaiderUsername { get; set; } = string.Empty;
    public int ViewerCount { get; set; }
}

public class OnBitsReceivedArgs : EventArgs
{
    public string Channel { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int Bits { get; set; }
    public string Message { get; set; } = string.Empty;
}

public interface ITwitchClient
{
    event EventHandler<OnMessageReceivedArgs> OnMessageReceived;
    event EventHandler<OnConnectedEventArgs> OnConnected;
    event EventHandler<OnDisconnectedArgs> OnDisconnected;

    Task ConnectAsync(string username, string oauthToken, string clientId, string channel);
    Task DisconnectAsync();
}

public class OnMessageReceivedArgs : EventArgs
{
    public ChatMessage ChatMessage { get; set; } = new ChatMessage();
}

public class ChatMessage
{
    public string Username { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public bool IsModerator { get; internal set; }
    public bool IsBroadcaster { get; internal set; }
    public bool IsSubscriber { get; internal set; }
    public bool IsVIP { get; internal set; }
    public List<KeyValuePair<string, string>> Badges { get; internal set; }
    public string Color { get; internal set; }
}