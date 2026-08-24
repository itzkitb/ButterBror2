using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Channels.SendChatMessage;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using TwitchLib.EventSub.Websockets;
using System.Threading.Channels;
using ButterBror.Core.Scopes;
using ButterBror.Data.Interfaces;

namespace ButterBror.ChatModules.Twitch.Services;

public sealed class TwitchClient : ITwitchClient, IDisposable
{
    // ><> constants & static fields
    private const string AppTokenCacheKey = "app";
    private static readonly TimeSpan TokenExpiryBuffer = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IrcFallbackDuration = TimeSpan.FromHours(1);
    private const int NormalChannelDelayMs = 1500;
    private const int ModVipChannelDelayMs = 100;
    
    // ><> dependencies & configuration
    private readonly TwitchConfiguration _config;
    private readonly ILogger<TwitchClient> _logger;
    private readonly ResiliencePipeline _twitchPipeline;
    private readonly ResiliencePipeline _apiPipeline;
    private ICustomDataRepository _db;
    private readonly HttpClient _tokenHttpClient = new();
    
    // ><> clients & api sdks
    private readonly TwitchLib.Client.TwitchClient _ircClient;
    private readonly EventSubWebsocketClient? _eventSubClient;
    private readonly TwitchAPI _api;

    // ><> collections & caches
    private readonly HashSet<string> _initialChannels;
    private readonly ConcurrentDictionary<string, string> _channelIdCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _broadcasterTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TwitchChannelSettings> _settingsCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StreamStatusInfo> _streamStatusCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _ircFallbackChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AppAccessTokenEntry> _appTokenCache = new(StringComparer.Ordinal);
    private readonly TimeSpan _statusCacheDuration = TimeSpan.FromMinutes(2);
    
    // ><> rate limit & queues
    private readonly ConcurrentDictionary<string, bool> _isModOrVipCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Channel<QueuedMessage>>> _channelQueues = new(StringComparer.OrdinalIgnoreCase);
    
    // ><> locks & state
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _appTokenRefreshLock = new(1, 1);
    private string _botId = string.Empty;
    private bool _isDisposed;
    private bool _isDisconencting;

    private sealed record QueuedMessage(
        string Channel,
        string Message,
        string? ReplyToMessageId,
        bool ConvertChannelId,
        TaskCompletionSource TaskCompletionSource
    );
    
    #region ><> Events
    public event EventHandler<Events.OnMessageReceivedArgs>? OnMessageReceived;
    public event EventHandler<OnConnectedEventArgs>? OnConnected;
    public event EventHandler<OnDisconnectedArgs>? OnDisconnected;
    public event EventHandler<Events.OnUserJoinedArgs>? OnUserJoined;
    public event EventHandler<Events.OnUserLeftArgs>? OnUserLeft;
    public event EventHandler<Events.OnNewSubscriberArgs>? OnNewSubscriber;
    public event EventHandler<Events.OnGiftedSubscriptionArgs>? OnGiftedSubscription;
    public event EventHandler<Events.OnRaidNotificationArgs>? OnRaidNotification;
    public event EventHandler<OnBitsReceivedArgs>? OnBitsReceived;
    public event EventHandler<BroadcasterAuthReceivedArgs>? OnBroadcasterAuthReceived;
    #endregion
    
    // ><> properties
    public bool IsConnected => _ircClient.IsConnected;
    public HashSet<string> ConnectedChannels => [.. _ircClient.JoinedChannels.Select(c => c.Channel.ToLowerInvariant())];
    
    // ><> constructor & finalizer
    private TwitchClient(
        IOptions<TwitchConfiguration> config,
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<TwitchClient> logger,
        IEnumerable<string> channels,
        ICustomDataRepository db)
    {
        _config = config.Value;
        _logger = logger;
        _twitchPipeline = pipelineProvider.GetPipeline("platform");
        _apiPipeline = pipelineProvider.GetPipeline("api");
        _initialChannels = [.. channels];
        _db = db;

        var reconnectionPolicy = new ReconnectionPolicy(
            minReconnectInterval: 3000,
            maxReconnectInterval: 10000,
            maxAttempts: int.MaxValue
        );
        var clientOptions = new ClientOptions(reconnectionPolicy);
        var websocketClient = new WebSocketClient(clientOptions);
        _ircClient = new TwitchLib.Client.TwitchClient(websocketClient);
        _api = new TwitchAPI();

        if (!string.IsNullOrWhiteSpace(_config.ClientSecret))
        {
            _eventSubClient = new EventSubWebsocketClient();
            _eventSubClient.WebsocketConnected += OnEventSubConnected;
            _eventSubClient.WebsocketReconnected += OnEventSubReconnected;
            _eventSubClient.WebsocketDisconnected += OnEventSubDisconnected;
            _eventSubClient.UserWhisperMessage += OnWhisperMessage;
        }

        SetupIrcListeners();
    }

    public static async Task<TwitchClient> CreateAsync(
        IOptions<TwitchConfiguration> config,
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<TwitchClient> logger,
        IEnumerable<string> channels,
        ICustomDataRepository db)
    {
        await using (new InitializationScope(logger, "twitch client"))
        {
            var client = new TwitchClient(config, pipelineProvider, logger, channels, db);
            return client;
        }
    }
    
    ~TwitchClient()
    {
        Dispose(false);
    }

    // ><> public api - connections
    public async Task ConnectAsync(string username, string oauthToken, string clientId)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        try
        {
            await using (new CustomScope(_logger, "conn", "twitch"))
            {
                // s0: init api with bot credentials
                _api.Settings.ClientId = clientId;
                _api.Settings.AccessToken = oauthToken;

                // s1: retrieve bot id
                _botId = await GetChannelIdAsync(username) ??
                         throw new InvalidOperationException("failed to retrieve bot user id");

                _logger.LogInformation("[tw] api init. id={Id}, name={Name}", _botId, username);

                // s2: init and connect
                var credentials = new ConnectionCredentials(
                    twitchUsername: username,
                    twitchOAuth: oauthToken,
                    disableUsernameCheck: true
                );
                _ircClient.Initialize(credentials);

                var connectTask = _ircClient.ConnectAsync();
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));

                if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                {
                    await _ircClient.DisconnectAsync();
                    throw new TimeoutException("irc connection timed out after 30 seconds");
                }

                await connectTask;

                // s3: connect eventsub
                if (_eventSubClient != null)
                {
                    await _eventSubClient.ConnectAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw] failed to connect");
            throw;
        }
    }
    public async Task DisconnectAsync()
    {
        if (_isDisposed)
            return;
        _isDisconencting = true;
        
        try
        {
            if (_ircClient.IsConnected)
            {
                await _ircClient.DisconnectAsync();
            }

            if (_eventSubClient != null)
            {
                await _eventSubClient.DisconnectAsync();
            }

            _logger.LogInformation("[tw] disconnected successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw] error during disconnection");
        }
    }
    
    // ><> public api - channels
    public async Task AddChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        
        var normalizedChannel = channel.ToLowerInvariant();
        if (!_initialChannels.Add(normalizedChannel)) 
            return;

        if (_ircClient.IsConnected)
            await ReconnectToChannelsAsync();
    }
    public async Task<bool> TryRemoveChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var normalizedChannel = channel.ToLowerInvariant();
        if (!_initialChannels.Contains(normalizedChannel))
            return true;

        if (_ircClient.IsConnected && IsJoined(channel))
            await _ircClient.LeaveChannelAsync(channel);
        
        return _initialChannels.Remove(channel);
    }
    public async Task JoinChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!_ircClient.IsConnected)
            throw new InvalidOperationException("irc is not connected yet");

        var normalizedChannel = channel.ToLowerInvariant();
        if (IsJoined(normalizedChannel))
            return;

        _logger.LogInformation("[tw] join #{Channel}", normalizedChannel);
        await _ircClient.JoinChannelAsync(normalizedChannel, true);
    }
    public async Task LeaveChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!IsJoined(channel))
            return;
        
        _logger.LogInformation("[tw] part #{Channel}", channel);
        if (_channelQueues.TryRemove(channel.ToLowerInvariant(), out var lazyQueue))
        {
            lazyQueue.Value.Writer.TryComplete();
        }
        await _ircClient.LeaveChannelAsync(channel);
    }
    public bool IsJoined(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return false;
        string normalizedChannel = channel.ToLowerInvariant();
        return _ircClient.JoinedChannels.Any(c => c.Channel.Equals(normalizedChannel, StringComparison.OrdinalIgnoreCase));
    }
    
    // ><> public api - messaging
    public async Task SendMessageAsync(string channel, string message, bool convertChannelId = true)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await EnqueueMessageAsync(channel, message, replyToMessageId: null, convertChannelId);
    }
    public async Task SendReplyAsync(string channel, string replyToMessageId, string message, bool convertChannelId = true)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (string.IsNullOrWhiteSpace(replyToMessageId))
        {
            _logger.LogWarning("[tw] send reply called with empty messageId, falling back");
            await SendMessageAsync(channel, message, convertChannelId);
            return;
        }
        await EnqueueMessageAsync(channel, message, replyToMessageId, convertChannelId);
    }
    private async Task EnqueueMessageAsync(string channel, string message, string? replyToMessageId, bool convertChannelId)
    {
        var normalizedChannel = channel.ToLowerInvariant();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new QueuedMessage(normalizedChannel, message, replyToMessageId, convertChannelId, tcs);
        
        var queue = _channelQueues.GetOrAdd(normalizedChannel, ch => new Lazy<Channel<QueuedMessage>>(() =>
        {
            var newChannel = Channel.CreateUnbounded<QueuedMessage>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            _ = ProcessChannelQueueAsync(ch, newChannel.Reader);
            return newChannel;
        }, LazyThreadSafetyMode.ExecutionAndPublication));
        
        await queue.Value.Writer.WriteAsync(item);
        await tcs.Task;
    }
    private async Task ProcessChannelQueueAsync(string channel, ChannelReader<QueuedMessage> reader)
    {
        long lastSentTimestamp = 0;

        try
        {
            await foreach (var item in reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    var isModOrVip = _isModOrVipCache.TryGetValue(channel, out var cached) && cached;
                    var requiredDelayMs = isModOrVip ? ModVipChannelDelayMs : NormalChannelDelayMs;

                    if (lastSentTimestamp > 0)
                    {
                        var elapsedMs = Stopwatch.GetElapsedTime(lastSentTimestamp).TotalMilliseconds;
                        if (elapsedMs < requiredDelayMs)
                        {
                            var delay = TimeSpan.FromMilliseconds(requiredDelayMs - elapsedMs);
                            await Task.Delay(delay, _cts.Token);
                        }
                    }

                    lastSentTimestamp = Stopwatch.GetTimestamp();

                    await SendHelixMessageAsync(item.Channel, item.Message, item.ReplyToMessageId, item.ConvertChannelId);
                    item.TaskCompletionSource.TrySetResult();
                }
                catch (OperationCanceledException)
                {
                    item.TaskCompletionSource.TrySetCanceled();
                    throw;
                }
                catch (Exception ex)
                {
                    item.TaskCompletionSource.TrySetException(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            
        }
    }
    public async Task SendWhisperAsync(string recipientUserId, string message)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        try
        {
            var sanitizedMessage = SanitizeMessage(message);
            await _twitchPipeline.ExecuteAsync(async _ =>
                await _api.Helix.Whispers.SendWhisperAsync(_botId, recipientUserId, sanitizedMessage, true));

            _logger.LogDebug("[tw] sent whisper to {Recipient}: \"{Message}\"", recipientUserId, sanitizedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw] failed to send whisper to {Recipient}", recipientUserId);
            throw;
        }
    }
    
    // ><> public api - tokens
    public void SetBroadcasterToken(string channelId, string token)
    {
        _broadcasterTokens[channelId] = token;
    }
    public string? GetBroadcasterToken(string channelId)
    {
        return _broadcasterTokens.GetValueOrDefault(channelId);
    }
    public async Task<bool> ValidateBroadcasterTokenAsync(string token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth", token);

            using var response = await _tokenHttpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[tw] failed to validate broadcaster token");
            return false;
        }
    }
    public async Task<string?> GetChannelIdAsync(string channelName)
    {
        try
        {
            var normalized = channelName.ToLowerInvariant();
            if (_channelIdCache.TryGetValue(normalized, out var cachedId))
            {
                return cachedId;
            }

            var channelUser = await _twitchPipeline.ExecuteAsync(async _ =>
                (await _api.Helix.Users.GetUsersAsync(logins: [normalized])).Users.FirstOrDefault());

            if (channelUser == null)
            {
                return null;
            }

            _channelIdCache[normalized] = channelUser.Id;
            return channelUser.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[tw] failed to get channel id for {Channel}", channelName);
            return null;
        }
    }
    public void InvalidateChannelSettingsCache(string channelId) => _settingsCache.TryRemove(channelId, out _);
    public void ClearIrcFallback(string channelId)
    {
        if (_ircFallbackChannels.TryRemove(channelId, out _))
        {
            _logger.LogInformation(
                "[tw] irc fallback cleared for channel {ChannelId}",
                channelId);
        }
    }
    
    // ><> messaging & fallbacks
    private async Task SendHelixMessageAsync(string channel, string message, string? replyToMessageId, bool convertChannelId = true)
    {
        var sanitizedMessage = SanitizeMessage(message);
        if (sanitizedMessage.Length > 500)
            sanitizedMessage = sanitizedMessage[..497] + "...";
        
        string? channelId = null;
        try
        {
            if (convertChannelId)
            {
                channelId = await GetChannelIdInternalAsync(channel);
            }
            else
            {
                channelId = channel;
            }

            var settings = await GetChannelSettingsAsync(channelId);
            if (!settings.AllowOffline || !settings.AllowOnline)
            {
                var isOnline = await IsChannelOnlineAsync(channel, channelId);

                switch (isOnline)
                {
                    case true when !settings.AllowOnline:
                        _logger.LogInformation("[tw] bot is disabled during online for #{Channel}. message ignored", channel);
                        return;
                    case false when !settings.AllowOffline:
                        _logger.LogInformation("[tw] bot is disabled during offline for #{Channel}. message ignored", channel);
                        return;
                }
            }
            
            if (IsIrcFallbackActive(channelId))
            {
                _logger.LogDebug("[tw] #{Channel} is in irc fallback mode, using irc", channel);
                await SendIrcMessage(channel, sanitizedMessage, replyToMessageId);
                return;
            }

            var appToken = await GetAppAccessTokenAsync();

            var request = new SendChatMessageRequest
            {
                BroadcasterId = channelId,
                SenderId = _botId,
                Message = sanitizedMessage,
                ReplyParentMessageId = replyToMessageId
            };

            await _apiPipeline.ExecuteAsync(async _ =>
                await _api.Helix.Chat.SendChatMessage(request, accessToken: appToken));

            _logger.LogDebug("[tw] sent message to #{Channel}: \"{Message}\"", channel, sanitizedMessage);
        }
        catch (Exception) when (channelId != null)
        {
            SetIrcFallback(channelId);

            try
            {
                await SendIrcMessage(channel, sanitizedMessage, replyToMessageId);
            }
            catch (Exception ircEx)
            {
                _logger.LogError(ircEx, "[tw] irc fallback also failed for #{Channel}", channel);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw] failed to resolve channel id for #{Channel}, cannot send message", channel);
            throw;
        }
    }
    private async Task SendIrcMessage(string channel, string message, string? replyToMessageId)
    {
        var normalizedChannel = channel.ToLowerInvariant();

        if (!IsJoined(normalizedChannel))
        {
            _logger.LogWarning(
                "[tw:fallback] bot is not joined to #{Channel}, cannot send via irc",
                normalizedChannel);
            return;
        }

        if (replyToMessageId != null)
        {
            await _ircClient.SendReplyAsync(normalizedChannel, replyToMessageId, message);
        }
        else
        {
            await _ircClient.SendMessageAsync(normalizedChannel, message);
        }
    }
    private void SetIrcFallback(string channelId)
    {
        var expiry = DateTime.UtcNow + IrcFallbackDuration;
        _ircFallbackChannels[channelId] = expiry;
        _logger.LogWarning(
            "[tw] irc fallback activated for channel #{ChannelId}. will expire at {Expiry:u}",
            channelId, expiry);
    }
    private bool IsIrcFallbackActive(string channelId)
    {
        if (!_ircFallbackChannels.TryGetValue(channelId, out var expiry))
            return false;

        if (DateTime.UtcNow < expiry)
            return true;
        
        _ircFallbackChannels.TryRemove(channelId, out _);
        _logger.LogInformation("[tw] irc fallback for channel #{ChannelId} expired, removed", channelId);
        return false;
    }
    private string SanitizeMessage(string message)
    {
        return Regex.Replace(message, @"\s+", " ")
            .Replace("\r", "")
            .Replace("\n", " ")
            .Trim();
    }
    
    // ><> twitch api & token helpers
    private async Task<string?> GetAppAccessTokenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.ClientSecret) || string.IsNullOrWhiteSpace(_config.ClientId))
        {
            _logger.LogInformation("[tw] aat requested but ClientId/ClientSecret not configured, falling back to bot token");
            return _config.OauthToken;
        }

        if (_appTokenCache.TryGetValue(AppTokenCacheKey, out var cached)
            && cached.ExpiresAt - DateTime.UtcNow > TokenExpiryBuffer)
        {
            return cached.Token;
        }

        await _appTokenRefreshLock.WaitAsync(ct);
        try
        {
            if (_appTokenCache.TryGetValue(AppTokenCacheKey, out cached)
                && cached.ExpiresAt - DateTime.UtcNow > TokenExpiryBuffer)
            {
                return cached.Token;
            }

            var entry = await FetchAppAccessTokenAsync(ct);
            _appTokenCache[AppTokenCacheKey] = entry;
            _logger.LogInformation("[tw] aat refreshed successfully. expires at {ExpiresAt:u}", entry.ExpiresAt);
            return entry.Token;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "[tw] aat receive error");
            throw;
        }
        finally
        {
            _appTokenRefreshLock.Release();
        }
    }
    private async Task<AppAccessTokenEntry> FetchAppAccessTokenAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("[tw] fetching new aat");
        using var content = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("client_id", _config.ClientId),
            new KeyValuePair<string, string>("client_secret", _config.ClientSecret),
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        ]);

        using var response = await _tokenHttpClient.PostAsync("https://id.twitch.tv/oauth2/token", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"twitch token endpoint returned http {(int)response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var tokenResponse = JsonSerializer.Deserialize<AppTokenResponse>(json)
            ?? throw new InvalidOperationException("aat response was null");

        if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("att response contained an empty access_token field");
        }

        return new AppAccessTokenEntry(
            Token: tokenResponse.AccessToken,
            ExpiresAt: DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn));
    }
    private async Task SubscribeToWhispersAsync()
    {
        if (_eventSubClient == null) return;

        await _apiPipeline.ExecuteAsync(async _ =>
            await _api.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "user.whisper.message", "1",
                new Dictionary<string, string> { { "user_id", _botId } },
                TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                _eventSubClient.SessionId
            )
        );
    }

    // ><> channels & settings helpers
    private async Task ReconnectToChannelsAsync()
    {
        var id = Guid.CreateVersion7();
        await using (new CustomScope(_logger, "tw:rejoin", $"process started (id:{id}, channels:{_initialChannels.Count})"))
        {
            foreach (var channel in _initialChannels)
            {
                try
                {
                    await _ircClient.JoinChannelAsync(channel, true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[tw] failed to rejoin channel #{Channel}", channel);
                }
            }
        }
    }
    private async Task<string> GetChannelIdInternalAsync(string channelName)
    {
        var normalized = channelName.ToLowerInvariant();
        if (_channelIdCache.TryGetValue(normalized, out var cachedId))
        {
            return cachedId;
        }

        var channelUser = await _twitchPipeline.ExecuteAsync(async _ =>
            (await _api.Helix.Users.GetUsersAsync(logins: [normalized])).Users.FirstOrDefault());

        if (channelUser == null)
        {
            throw new InvalidOperationException($"channel #{normalized} not found");
        }

        _channelIdCache[normalized] = channelUser.Id;
        return channelUser.Id;
    }
    private async ValueTask<TwitchChannelSettings> GetChannelSettingsAsync(string channelId)
    {
        if (_settingsCache.TryGetValue(channelId, out var cached)) return cached;

        try
        {
            var json = await _db.GetDataAsync($"twitch:settings:{channelId}");
            var settings = !string.IsNullOrWhiteSpace(json) 
                ? JsonSerializer.Deserialize<TwitchChannelSettings>(json) 
                : new TwitchChannelSettings();
            
            _settingsCache[channelId] = settings!;
            return settings!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[tw] failed to load settings from Redis for #{ChannelId}", channelId);
            return new TwitchChannelSettings();
        }
    }
    private async Task<bool> IsChannelOnlineAsync(string channelLogin, string channelId)
    {
        if (_streamStatusCache.TryGetValue(channelId, out var cached) &&
            DateTime.UtcNow - cached.LastChecked < _statusCacheDuration)
        {
            return cached.IsOnline;
        }

        try
        {
            var res = await _twitchPipeline.ExecuteAsync(async _ =>
                (await _api.Helix.Streams.GetStreamsAsync(userIds: [channelId])));
            var isOnline = res.Streams is { Length: > 0 };

            _streamStatusCache[channelId] = new StreamStatusInfo
            {
                IsOnline = isOnline,
                LastChecked = DateTime.UtcNow
            };

            return isOnline;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw] error checking stream status for #{Channel}", channelLogin);
            return false;
        }
    }
    
    // ><> irc
    private void SetupIrcListeners()
    {
        _ircClient.OnMessageReceived += OnClientMessageReceived;
        _ircClient.OnConnected += OnClientConnected;
        _ircClient.OnDisconnected += OnClientDisconnected;
        _ircClient.OnReconnected += OnClientReconnected;
        _ircClient.OnConnectionError += OnClientConnectionError;
        _ircClient.OnJoinedChannel += OnClientJoinedChannel;
        _ircClient.OnLeftChannel += OnClientPartChannel;
        _ircClient.OnNewSubscriber += OnClientNewSubscriber;
        _ircClient.OnGiftedSubscription += OnClientGiftedSubscription;
        _ircClient.OnRaidNotification += OnClientRaidNotification;
        _ircClient.OnBitsBadgeTier += OnClientBitsReceived;
        _ircClient.OnUserStateChanged += OnClientUserStateChanged;
    }
    private Task OnClientUserStateChanged(object? sender, OnUserStateChangedArgs e)
    {
        try
        {
            var channel = e.UserState.Channel.ToLowerInvariant();
            var isMod = e.UserState.IsModerator;
            var isVip = e.UserState.Badges.Any(b => b.Key.Equals("vip", StringComparison.OrdinalIgnoreCase));

            _isModOrVipCache[channel] = isMod || isVip;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw:irc] error parsing USERSTATE for #{Channel}", e.UserState.Channel);
        }

        return Task.CompletedTask;
    }
    private async Task OnClientConnected(object? sender, OnConnectedEventArgs e)
    {
        try
        {
            _logger.LogInformation("[tw:irc] client connected");
            OnConnected?.Invoke(this, e);
            await ReconnectToChannelsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw:irc] error handling");
        }
    }
    private async Task OnClientReconnected(object? sender, OnConnectedEventArgs e)
    {
        try
        {
            _logger.LogInformation("[tw:irc] client reconnected");
            await ReconnectToChannelsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw:irc] error handling reconnect");
        }
    }
    private async Task OnClientDisconnected(object? sender, OnDisconnectedArgs e)
    {
        _logger.LogWarning("[tw:irc] client disconnected");
        OnDisconnected?.Invoke(this, e);

        // s0. nothing
        if (_isDisposed | _isDisconencting)
            return;

        // s1. huh?
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            if (!_ircClient.IsConnected && !_isDisposed)
            {
                _logger.LogWarning("[tw:irc] client is still disconnected. triggering manual reconnect...");
                try
                {
                    await _ircClient.ReconnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[tw:irc] manual reconnect attempt failed");
                }
            }
        });
    }
    private Task OnClientMessageReceived(object? sender, TwitchLib.Client.Events.OnMessageReceivedArgs e)
    {
        try
        {
            // convert twitchlib sht
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
                Badges = e.ChatMessage.Badges,
                Color = e.ChatMessage.HexColor,
                MessageId = e.ChatMessage.Id
            };

            OnMessageReceived?.Invoke(this, new Events.OnMessageReceivedArgs { ChatMessage = chatMessage });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw:irc] error handling message");
        }

        return Task.CompletedTask;
    }
    private Task OnClientConnectionError(object? sender, OnConnectionErrorArgs e)
    {
        _logger.LogError("[tw:irc] connection error: {Error}", e.Error);
        
        return Task.CompletedTask;
    }
    private Task OnClientJoinedChannel(object? sender, OnJoinedChannelArgs e)
    {
        var channel = e.Channel.ToLowerInvariant();
        _logger.LogDebug("[tw:irc] joined #{Channel}", channel);
        
        return Task.CompletedTask;
    }
    private Task OnClientPartChannel(object? sender, OnLeftChannelArgs e)
    {
        var channel = e.Channel.ToLowerInvariant();
        _logger.LogDebug("[tw:irc] parted #{Channel}", channel);
        
        return Task.CompletedTask;
    }
    private Task OnClientNewSubscriber(object? sender, TwitchLib.Client.Events.OnNewSubscriberArgs e)
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
            _logger.LogError(ex, "[tw:irc] error handling new subscriber event");
        }
        
        return Task.CompletedTask;
    }
    private Task OnClientGiftedSubscription(object? sender, TwitchLib.Client.Events.OnGiftedSubscriptionArgs e)
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
            _logger.LogError(ex, "[tw:irc] error handling gifted subscription event");
        }
        
        return Task.CompletedTask;
    }
    private Task OnClientRaidNotification(object? sender, TwitchLib.Client.Events.OnRaidNotificationArgs e)
    {
        try
        {
            var args = new Events.OnRaidNotificationArgs
            {
                Channel = e.Channel,
                RaiderUsername = e.RaidNotification.Login,
                ViewerCount = int.Parse(e.RaidNotification.MsgParamViewerCount)
            };
            OnRaidNotification?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw:irc] error handling raid notification event");
        }
        
        return Task.CompletedTask;
    }
    private Task OnClientBitsReceived(object? sender, OnBitsBadgeTierArgs e)
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
            _logger.LogError(ex, "[tw:irc] error handling bits received event");
        }
        
        return Task.CompletedTask;
    }
    
    // ><> EventSub
    private async Task OnEventSubReconnected(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketReconnectedArgs e)
    {
        _logger.LogInformation("[tw:es] client reconnected");

        await SubscribeToWhispersAsync();
    }
    private async Task OnEventSubConnected(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketConnectedArgs e)
    {
        _logger.LogInformation("[tw:es] client connected");

        await SubscribeToWhispersAsync();
    }
    private async Task OnEventSubDisconnected(object? sender, EventArgs e)
    {
        _logger.LogWarning("[tw:es] client disconnected");
        try
        {
            await Task.Delay(2000);
            if (_eventSubClient != null)
                await _eventSubClient.ConnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw:es] failed to reconnect");
        }
    }
    private Task OnWhisperMessage(object? sender, TwitchLib.EventSub.Core.EventArgs.User.UserWhisperMessageArgs e)
    {
        try
        {
            var data = e.Payload.Event;
            var message = data.Whisper.Text.Trim();

            // s0: try to decode b64
            string json;
            try
            {
                var bytes = Convert.FromBase64String(message);
                json = System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                _logger.LogDebug("[tw:es] whisper message is not a valid b64 string, ignoring");
                return Task.CompletedTask;
            }

            // s1: parse json
            var authData = JsonSerializer.Deserialize<BroadcasterAuthPayload>(json);
            if (authData == null || string.IsNullOrWhiteSpace(authData.Channel) || string.IsNullOrWhiteSpace(authData.Token))
            {
                _logger.LogWarning("[tw:es] invalid auth payload format in whisper from @{User}", data.FromUserName);
                return Task.CompletedTask;
            }

            // s2: trigger the auth event
            var args = new BroadcasterAuthReceivedArgs
            {
                UserId = data.FromUserId,
                Username = data.FromUserName,
                Channel = authData.Channel,
                Token = authData.Token
            };

            OnBroadcasterAuthReceived?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw:es] error handling whisper");
        }

        return Task.CompletedTask;
    }

    // ><> disposable
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    private void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (disposing)
        {
            _isDisposed = true;
            _cts.Cancel();
            
            foreach (var lazyQueue in _channelQueues.Values)
            {
                lazyQueue.Value.Writer.TryComplete();
            }
            
            _appTokenRefreshLock.Dispose();
            _tokenHttpClient.Dispose();
            if (_ircClient.IsConnected)
            {
                _ = _ircClient.DisconnectAsync();
            }
            _logger.LogInformation("[tw] client disposed successfully");
        }
        _isDisposed = true;
    }
}