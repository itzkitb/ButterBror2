using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using TwitchLib.Client.Events;

namespace ButterBror.ChatModules.Twitch.Services;

public class TwitchLibClient : ITwitchWhisperClient, IDisposable
{
    private ITwitchClient _ircClient;
    private ITwitchWhisperClient _botClient;

    private readonly TwitchConfiguration _config;

    private readonly ResiliencePipeline _twitchPipeline;
    private readonly ResiliencePipeline _apiPipeline;
    private readonly ILogger<TwitchLibClient> _logger;
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

    public bool IsConnected => _ircClient.IsConnected;

    public HashSet<string> ConnectedChannels => _ircClient.ConnectedChannels.Concat(_botClient.ConnectedChannels).ToHashSet();

    private bool _eventSubConnected = false;

    public bool IsJoined(string channel) => _ircClient.IsJoined(channel) || _botClient.IsJoined(channel);

    public TwitchLibClient(
        IOptions<TwitchConfiguration> config,
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<TwitchLibClient> logger,
        IEnumerable<string> IrcChannels,
        IEnumerable<string> eventSubChannels)
    {
        _config = config.Value;
        _logger = logger;
        _twitchPipeline = pipelineProvider.GetPipeline("platform");
        _apiPipeline = pipelineProvider.GetPipeline("api");

        _ircClient = new TwitchIrcClient(IrcChannels?.ToList() ?? new List<string>(), logger);
        _botClient = new TwitchBotClient(
            eventSubChannels?.ToList() ?? new List<string>(),
            logger, _twitchPipeline, _apiPipeline, _ircClient);

        _logger.LogInformation("[TWL] Hello, world!");
    }

    public async Task ConnectAsync(string username, string oauthToken, string clientId)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        try
        {
            _logger.LogInformation("[TWL] Connecting to twitch.tv...");

            // Config check
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(oauthToken) || string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException("Twitch username and OAuth token/ClientId are required");
            }

            SetupListeners();

            await _ircClient.ConnectAsync(username, oauthToken, clientId);
            await _botClient.ConnectAsync(username, oauthToken, clientId);

            if (_config.Channel != null)
            {
                await JoinChannelAsync(_config.Channel);
            }
            else
            {
                await _botClient.JoinChannelAsync(username);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWL] Failed to connect to twitch.tv!");
            throw;
        }
    }

    private void SetupListeners()
    {
        // IRC
        _ircClient.OnDisconnected += OnDisconnected;
        _ircClient.OnUserLeft += OnUserLeft;
        _ircClient.OnConnected += OnConnected;
        _ircClient.OnBitsReceived += OnBitsReceived;
        _ircClient.OnGiftedSubscription += OnGiftedSubscription;
        _ircClient.OnMessageReceived += OnMessageReceived;
        _ircClient.OnNewSubscriber += OnNewSubscriber;
        _ircClient.OnRaidNotification += OnRaidNotification;
        _ircClient.OnUserJoined += OnUserJoined;

        // EventSub
        _botClient.OnDisconnected += OnDisconnected;
        _botClient.OnUserLeft += OnUserLeft;
        _botClient.OnConnected += OnConnected;
        _botClient.OnBitsReceived += OnBitsReceived;
        _botClient.OnGiftedSubscription += OnGiftedSubscription;
        _botClient.OnMessageReceived += OnMessageReceived;
        _botClient.OnNewSubscriber += OnNewSubscriber;
        _botClient.OnRaidNotification += OnRaidNotification;
        _botClient.OnUserJoined += OnUserJoined;
    }

    public async Task DisconnectAsync()
    {
        if (_isDisposed) return;

        try
        {
            if (_ircClient.IsConnected)
            {
                await _ircClient.DisconnectAsync();
            }

            if (_eventSubConnected)
            {
                await _botClient.DisconnectAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWL] Error during disconnection");
        }
    }

    public async Task JoinChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        try
        {
            await _botClient.JoinChannelAsync(channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWL] Failed to join #{Channel}", channel);
            throw;
        }
    }

    public async Task LeaveChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        try
        {
            if (_botClient.ConnectedChannels.Contains(channel, StringComparer.OrdinalIgnoreCase))
            {
                await _botClient.LeaveChannelAsync(channel);
            }
            else
            {
                await _ircClient.LeaveChannelAsync(channel);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWL] Failed to leave #{Channel}", channel);
            throw;
        }
    }

    public async Task SendMessageAsync(string channel, string message)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_botClient.ConnectedChannels.Contains(channel, StringComparer.OrdinalIgnoreCase))
        {
            if (!_botClient.IsJoined(channel))
            {
                await _botClient.JoinChannelAsync(channel);
            }

            await _botClient.SendMessageAsync(channel, message);
        }
        else
        {
            if (!_ircClient.IsJoined(channel))
            {
                await _ircClient.JoinChannelAsync(channel);
            }

            await _ircClient.SendMessageAsync(channel, message);
        }
    }

    public async Task SendReplyAsync(string channel, string replyToMessageId, string message)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_botClient.ConnectedChannels.Contains(channel, StringComparer.OrdinalIgnoreCase))
        {
            if (!_botClient.IsJoined(channel))
            {
                await _botClient.JoinChannelAsync(channel);
            }

            await _botClient.SendReplyAsync(channel, replyToMessageId, message);
        }
        else
        {
            if (!_ircClient.IsJoined(channel))
            {
                await _ircClient.JoinChannelAsync(channel);
            }

            await _ircClient.SendReplyAsync(channel, replyToMessageId, message);
        }
    }

    public async Task SendWhisperAsync(string recipientUserId, string message)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        try
        {
            await _botClient.SendWhisperAsync(recipientUserId, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TWL] [EventSub] Failed to send whisper to @{Channel}", recipientUserId);
            throw;
        }
    }

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

                if (_ircClient.IsConnected)
                {
                    _ = _ircClient.DisconnectAsync();
                }

                _logger.LogInformation("[TWL] Client disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TWL] Error during client disposal");
            }
        }

        _isDisposed = true;
    }

    ~TwitchLibClient()
    {
        Dispose(false);
    }
    #endregion Dispose
}
