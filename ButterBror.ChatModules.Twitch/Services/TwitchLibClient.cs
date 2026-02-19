using System.Text.RegularExpressions;
using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using TwitchLib.EventSub.Websockets;

namespace ButterBror.ChatModules.Twitch.Services;

public class TwitchLibClient : ITwitchClient, IDisposable
{
    private TwitchClient _client = null!;
    private TwitchAPI _clientAPI = null!;
    private EventSubWebsocketClient _eventSubClient = null!;
    private readonly TwitchConfiguration _config;

    private readonly ResiliencePipeline _twitchPipeline;
    private readonly ResiliencePipeline _apiPipeline;
    private readonly ILogger<TwitchLibClient> _logger;
    private bool _isDisposed;

    private string _username = string.Empty;
    private string _id = string.Empty;

    public event EventHandler<Events.OnMessageReceivedArgs>? OnMessageReceived;
    public event EventHandler<OnConnectedEventArgs>? OnConnected;
    public event EventHandler<OnDisconnectedArgs>? OnDisconnected;
    public event EventHandler<Events.OnUserJoinedArgs>? OnUserJoined;
    public event EventHandler<Events.OnUserLeftArgs>? OnUserLeft;
    public event EventHandler<Events.OnNewSubscriberArgs>? OnNewSubscriber;
    public event EventHandler<Events.OnGiftedSubscriptionArgs>? OnGiftedSubscription;
    public event EventHandler<Events.OnRaidNotificationArgs>? OnRaidNotification;
    public event EventHandler<OnBitsReceivedArgs>? OnBitsReceived;

    public TwitchLibClient(
        IOptions<TwitchConfiguration> config,
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<TwitchLibClient> logger)
    {
        _config = config.Value;
        _logger = logger;
        _twitchPipeline = pipelineProvider.GetPipeline("platform");
        _apiPipeline = pipelineProvider.GetPipeline("api");

        SetupClient();
        SetupSubscribes();

        _logger.LogInformation("Hello, Twitch! <3");
    }

    private void SetupClient()
    {
        var clientOptions = new ClientOptions();

        var websocketClient = new WebSocketClient(clientOptions);
        _client = new TwitchClient(websocketClient);
        _clientAPI = new TwitchAPI();
        _eventSubClient = new EventSubWebsocketClient();
    }

    private void SetupSubscribes()
    {
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
    }

    public async Task ConnectAsync(string username, string oauthToken, string clientId, string channel)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(TwitchLibClient));

        try
        {
            _logger.LogInformation("Attempting to connect to Twitch channel: {Channel}", channel);

            // Config check
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(oauthToken) || string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException("Twitch username and OAuth token/ClientId are required");
            }

            // Setup credentials
            var credentials = new ConnectionCredentials(
                twitchUsername: username,
                twitchOAuth: oauthToken,
                disableUsernameCheck: true
            );

            _client.Initialize(credentials, channel);

            // Connecting
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
            // Subscribe to whispers
            await _apiPipeline.ExecuteAsync(async ct =>
                await _clientAPI.Helix.EventSub.CreateEventSubSubscriptionAsync(
                    "user.whisper.message", "1",
                    new Dictionary<string, string> { { "user_id", _id } },
                    TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                    _eventSubClient.SessionId
                )
            );
        };

        // Hook whisper event
        _eventSubClient.UserWhisperMessage += async (s, e) =>
        {
            var msg = e.Payload.Event;
            _logger.LogDebug($"Whisper from {msg.FromUserName}: {msg.Whisper.Text}");
            _ = SendWhisperAsync(msg.FromUserId, "Hi!");

            await Task.CompletedTask;
        };

        // Connect to the EventSub
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
                _logger.LogInformation("Bye bye Twitch peepoSad");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Twitch disconnection");
        }
    }

    public async Task JoinChannelAsync(string channel)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(TwitchLibClient));

        if (!_client.IsConnected)
            throw new InvalidOperationException("Not connected to Twitch");

        try
        {
            await _client.JoinChannelAsync(channel);
            _logger.LogInformation("Joining channel: {Channel}", channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join channel {Channel}", channel);
            throw;
        }
    }

    public async Task LeaveChannelAsync(string channel)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(TwitchLibClient));

        if (!_client.IsConnected)
            throw new InvalidOperationException("Not connected to Twitch");

        try
        {
            await _client.LeaveChannelAsync(channel);
            _logger.LogInformation("Leaving channel: {Channel}", channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave channel {Channel}", channel);
            throw;
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
            // Clearing a message of prohibited characters
            var sanitizedMessage = SanitizeMessage(message);

            // Checking the length of the message
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
            // Convert twitchlib shit
            var chatMessage = new Models.ChatMessage
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

            OnMessageReceived?.Invoke(this, new Events.OnMessageReceivedArgs { ChatMessage = chatMessage });
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
            var args = new Events.OnNewSubscriberArgs
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
            var args = new Events.OnGiftedSubscriptionArgs
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
            var args = new Events.OnRaidNotificationArgs
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

    private string SanitizeMessage(string message)
    {
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
