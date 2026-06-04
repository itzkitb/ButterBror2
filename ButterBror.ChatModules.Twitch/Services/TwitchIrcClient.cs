using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;

namespace ButterBror.ChatModules.Twitch.Services;

public class TwitchIrcClient : ITwitchClient, IDisposable
{
    public readonly List<string> Channels;
    private readonly ILogger<TwitchLibClient> _logger;

    private TwitchClient _client = null!;
    private bool _isDisposed;

    public event EventHandler<Events.OnMessageReceivedArgs>? OnMessageReceived;
    public event EventHandler<OnConnectedEventArgs>? OnConnected;
    public event EventHandler<OnDisconnectedArgs>? OnDisconnected;
    public event EventHandler<Events.OnUserJoinedArgs>? OnUserJoined;
    public event EventHandler<Events.OnUserLeftArgs>? OnUserLeft;
    public event EventHandler<Events.OnNewSubscriberArgs>? OnNewSubscriber;
    public event EventHandler<Events.OnGiftedSubscriptionArgs>? OnGiftedSubscription;
    public event EventHandler<Events.OnRaidNotificationArgs>? OnRaidNotification;
    public event EventHandler<OnBitsReceivedArgs>? OnBitsReceived;

    public HashSet<string> ConnectedChannels => _client.JoinedChannels
        .Select(c => c.Channel.ToLowerInvariant())
        .ToHashSet();
    public bool IsConnected => _client.IsConnected;

    public TwitchIrcClient(
        IEnumerable<string> channels,
        ILogger<TwitchLibClient> logger)
    {
        _logger = logger;

        var clientOptions = new ClientOptions(new ReconnectionPolicy(1000));
        var websocketClient = new WebSocketClient(clientOptions);

        _client = new TwitchClient(websocketClient);
        Channels = channels?.ToList() ?? new List<string>();
        SetupSubscribes();
    }

    #region Public

    public bool IsJoined(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return false;

        string normalizedChannel = channel.ToLowerInvariant();
        return _client.JoinedChannels.Any(c => c.Channel.Equals(normalizedChannel, StringComparison.OrdinalIgnoreCase));
    }

    public async Task ConnectAsync(string username, string oauthToken, string clientId)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        try
        {
            _logger.LogInformation("[TWIRC] Connecting to Irc for {Count} channels", Channels.Count);

            var credentials = new ConnectionCredentials(
                twitchUsername: username,
                twitchOAuth: oauthToken,
                disableUsernameCheck: true
            );

            _client.Initialize(credentials);

            var connectTask = _client.ConnectAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));

            if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
            {
                await _client.DisconnectAsync();
                throw new TimeoutException("Irc connection timed out after 30 seconds");
            }

            await connectTask;

            foreach (var channel in Channels)
            {
                await JoinChannelAsync(channel);
            }

            _logger.LogInformation("[TWIRC] Irc connected successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWIRC] Failed to connect to Irc");
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _client.DisconnectAsync();
        _logger.LogInformation("[TWIRC] Bye bye");
    }

    public async Task JoinChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!_client.IsConnected)
        {
            throw new Exception("Twitch isn't connected via IRC yet");
        }

        var normalizedChannel = channel.ToLowerInvariant();
        if (IsJoined(normalizedChannel))
        {
            return;
        }

        await _client.JoinChannelAsync(normalizedChannel, true);
        _logger.LogInformation("[TWIRC] Connected to #{Channel} via Irc", normalizedChannel);
    }

    public async Task LeaveChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _client.LeaveChannelAsync(channel);
        _logger.LogInformation("[TWIRC] Leaving #{Channel}", channel);
    }

    public async Task SendMessageAsync(string channel, string message)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

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
            _logger.LogDebug("[TWIRC] Sent message to #{Channel}: \"{Message}\"", channel, sanitizedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWIRC] Failed to send message to #{Channel}", channel);
            throw;
        }
    }

    public async Task SendReplyAsync(string channel, string replyToMessageId, string message)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!_client.IsConnected)
            throw new InvalidOperationException("Not connected to Twitch");

        if (string.IsNullOrWhiteSpace(replyToMessageId))
        {
            _logger.LogWarning("[TWIRC] SendReplyAsync called with empty messageId, falling back to SendMessageAsync");
            await SendMessageAsync(channel, message);
            return;
        }

        try
        {
            var sanitizedMessage = SanitizeMessage(message);

            if (sanitizedMessage.Length > 500)
            {
                sanitizedMessage = sanitizedMessage[..497] + "...";
            }

            await _client.SendReplyAsync(channel, replyToMessageId, sanitizedMessage);
            _logger.LogDebug("[TWIRC] Sent reply to #{Channel} (parent={MsgId}): \"{Message}\"",
                channel, replyToMessageId, sanitizedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWIRC] Failed to send reply to #{Channel} (parent={MsgId})", channel, replyToMessageId);
            throw;
        }
    }

    #endregion Public

    #region Tools

    private void SetupSubscribes()
    {
        _client.OnMessageReceived += OnClientMessageReceived;
        _client.OnConnected += OnClientConnected;
        _client.OnDisconnected += OnClientDisconnected;
        _client.OnConnectionError += OnClientConnectionError;
        _client.OnJoinedChannel += OnClientJoinedChannel;
        _client.OnLeftChannel += OnClientPartChannel;
        _client.OnNewSubscriber += OnClientNewSubscriber;
        _client.OnGiftedSubscription += OnClientGiftedSubscription;
        _client.OnRaidNotification += OnClientRaidNotification;
        _client.OnBitsBadgeTier += OnClientBitsReceived;
    }

    private string SanitizeMessage(string message)
    {
        return Regex.Replace(message, @"\s+", " ")
            .Replace("\r", "")
            .Replace("\n", " ")
            .Trim();
    }

    #endregion Tools

    #region Events

    private async Task OnClientMessageReceived(object? sender, TwitchLib.Client.Events.OnMessageReceivedArgs e)
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
                ChannelId = e.ChatMessage.RoomId,
                IsModerator = e.ChatMessage.UserDetail.IsModerator,
                IsBroadcaster = e.ChatMessage.IsBroadcaster,
                IsSubscriber = e.ChatMessage.UserDetail.IsSubscriber,
                IsVip = e.ChatMessage.UserDetail.IsVip,
                Badges = e.ChatMessage.BadgeInfo,
                Color = e.ChatMessage.HexColor,
                MessageId = e.ChatMessage.Id
            };

            OnMessageReceived?.Invoke(this, new Events.OnMessageReceivedArgs { ChatMessage = chatMessage });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWIRC] Error handling Twitch message");
        }
    }

    private async Task OnClientConnected(object? sender, OnConnectedEventArgs e)
    {
        try
        {
            _logger.LogInformation("[TWIRC] Connected to Irc");
            OnConnected?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWIRC] Error handling Irc");
        }
    }

    private async Task OnClientDisconnected(object? sender, OnDisconnectedArgs e)
    {
        _logger.LogWarning("[TWIRC] Disconnected");
    }

    private async Task OnClientConnectionError(object? sender, OnConnectionErrorArgs e)
    {
        _logger.LogError("[TWIRC] Connection error: {Error}", e.Error);
    }

    private async Task OnClientJoinedChannel(object? sender, OnJoinedChannelArgs e)
    {
        var channel = e.Channel.ToLowerInvariant();
        _logger.LogInformation("[TWIRC] Joined #{Channel}", channel);
    }

    private async Task OnClientPartChannel(object? sender, OnLeftChannelArgs e)
    {
        var channel = e.Channel.ToLowerInvariant();
        _logger.LogInformation("[TWIRC] Parted #{Channel}", channel);
    }

    private async Task OnClientNewSubscriber(object? sender, TwitchLib.Client.Events.OnNewSubscriberArgs e)
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
            _logger.LogError(ex, "[TWIRC] Error handling new subscriber event");
        }
    }

    private async Task OnClientGiftedSubscription(object? sender, TwitchLib.Client.Events.OnGiftedSubscriptionArgs e)
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
            _logger.LogError(ex, "[TWIRC] Error handling gifted subscription event");
        }
    }

    private async Task OnClientRaidNotification(object? sender, TwitchLib.Client.Events.OnRaidNotificationArgs e)
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
            _logger.LogError(ex, "[TWIRC] Error handling raid notification event");
        }
    }

    private async Task OnClientBitsReceived(object? sender, OnBitsBadgeTierArgs e)
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
            _logger.LogError(ex, "[TWIRC] Error handling bits received event");
        }
    }

    #endregion Events

    #region Dispose

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
                    _ = _client.DisconnectAsync();
                }

                _logger.LogInformation("[TWIRC] Client disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TWIRC] Error during client disposal");
            }
        }

        _isDisposed = true;
    }

    ~TwitchIrcClient()
    {
        Dispose(false);
    }

    #endregion Dispose
}
