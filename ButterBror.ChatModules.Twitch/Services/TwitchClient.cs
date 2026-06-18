using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ButterBror.Data;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Channels.SendChatMessage;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using TwitchLib.EventSub.Websockets;

namespace ButterBror.ChatModules.Twitch.Services;

public class TwitchClient : ITwitchWhisperClient, IDisposable
{
    private readonly TwitchConfiguration _config;
    private readonly ILogger<TwitchClient> _logger;
    private readonly ResiliencePipeline _twitchPipeline;
    private readonly ResiliencePipeline _apiPipeline;
    private ICustomDataRepository _db = null!;

    private readonly TwitchLib.Client.TwitchClient _ircClient;
    private readonly EventSubWebsocketClient? _eventSubClient;
    private readonly TwitchAPI _api;

    private string _botId = string.Empty;
    private bool _isDisposed;

    private readonly ConcurrentDictionary<string, string> _channelIdCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _broadcasterTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _initialChannels;
    private readonly ConcurrentDictionary<string, TwitchChannelSettings> _settingsCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StreamStatusInfo> _streamStatusCache = new(StringComparer.Ordinal);
    private readonly TimeSpan _statusCacheDuration = TimeSpan.FromMinutes(2);

    // IRC fallback: channelId -> UTC expiry time
    // When Helix API fails, the channel is switched to IRC for IrcFallbackDuration.
    // The fallback is cleared early if the broadcaster re-authenticates via whisper.
    private readonly ConcurrentDictionary<string, DateTime> _ircFallbackChannels = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan IrcFallbackDuration = TimeSpan.FromHours(1);

    private sealed record AppAccessTokenEntry(string Token, DateTime ExpiresAt);
    private readonly ConcurrentDictionary<string, AppAccessTokenEntry> _appTokenCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _appTokenRefreshLock = new(1, 1);
    private const string AppTokenCacheKey = "app";
    private static readonly TimeSpan TokenExpiryBuffer = TimeSpan.FromMinutes(5);
    private readonly HttpClient _tokenHttpClient = new();

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

    public bool IsConnected => _ircClient.IsConnected;

    public HashSet<string> ConnectedChannels => [.. _ircClient.JoinedChannels.Select(c => c.Channel.ToLowerInvariant())];

    public TwitchClient(
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

        var clientOptions = new ClientOptions(new ReconnectionPolicy(1000));
        var websocketClient = new WebSocketClient(clientOptions);
        _ircClient = new TwitchLib.Client.TwitchClient(websocketClient);
        _api = new TwitchAPI();

        if (!string.IsNullOrWhiteSpace(_config.ClientSecret))
        {
            _eventSubClient = new EventSubWebsocketClient();
            _eventSubClient.WebsocketConnected += OnEventSubConnected;
            _eventSubClient.WebsocketReconnected += OnEventSubReconnected;
            _eventSubClient.UserWhisperMessage += OnWhisperMessage;
        }

        SetupIrcListeners();
        _logger.LogInformation("[TW] Twitch client initialized");
    }

    public async Task ConnectAsync(string username, string oauthToken, string clientId)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        try
        {
            _logger.LogInformation("[TW] Connecting to Twitch...");

            // S0. Initialize API with bot credentials
            _api.Settings.ClientId = clientId;
            _api.Settings.AccessToken = oauthToken;

            // S1. Retrieve and cache Bot ID
            _botId = await GetChannelIdAsync(username) ?? throw new InvalidOperationException("Failed to retrieve bot user ID");

            _logger.LogInformation("[TW] API initialized. BotId={Id}, Name={Name}", _botId, username);

            // S2. Initialize and connect for real-time message receiving
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
                throw new TimeoutException("IRC connection timed out after 30 seconds");
            }

            await connectTask;
            _logger.LogInformation("[TW] Connected successfully");

            // S3. Connect EventSub for Whispers
            if (_eventSubClient != null)
            {
                await _eventSubClient.ConnectAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TW] Failed to connect to Twitch");
            throw;
        }
    }

    private async Task SubscribeToWhispersAsync()
    {
        if (_eventSubClient == null) return;

        await _apiPipeline.ExecuteAsync(async ct =>
            await _api.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "user.whisper.message", "1",
                new Dictionary<string, string> { { "user_id", _botId } },
                TwitchLib.Api.Core.Enums.EventSubTransportMethod.Websocket,
                _eventSubClient.SessionId
            )
        );
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
            _logger.LogWarning(ex, "[TW] Failed to validate broadcaster token");
            return false;
        }
    }

    public async Task JoinChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!_ircClient.IsConnected)
            throw new InvalidOperationException("IRC is not connected yet");

        var normalizedChannel = channel.ToLowerInvariant();
        if (IsJoined(normalizedChannel))
            return;

        await _ircClient.JoinChannelAsync(normalizedChannel, true);
        _logger.LogInformation("[TW] Joined #{Channel}", normalizedChannel);
    }

    public async Task LeaveChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!IsJoined(channel))
            return;
        
        await _ircClient.LeaveChannelAsync(channel);
        _logger.LogInformation("[TW] Left #{Channel}", channel);
    }

    public bool IsJoined(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return false;
        string normalizedChannel = channel.ToLowerInvariant();
        return _ircClient.JoinedChannels.Any(c => c.Channel.Equals(normalizedChannel, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SendMessageAsync(string channel, string message)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await SendHelixMessageAsync(channel, message, replyToMessageId: null);
    }

    public async Task SendReplyAsync(string channel, string replyToMessageId, string message)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (string.IsNullOrWhiteSpace(replyToMessageId))
        {
            _logger.LogWarning("[TW] SendReplyAsync called with empty messageId, falling back to SendMessageAsync");
            await SendMessageAsync(channel, message);
            return;
        }
        await SendHelixMessageAsync(channel, message, replyToMessageId);
    }

    private async Task SendHelixMessageAsync(string channel, string message, string? replyToMessageId)
    {
        // Sanitize and truncate once — the same result is used for both Helix and IRC paths.
        var sanitizedMessage = SanitizeMessage(message);
        if (sanitizedMessage.Length > 500)
            sanitizedMessage = sanitizedMessage[..497] + "...";

        // channelId is resolved before the API call so the fallback catcher can register it.
        string? channelId = null;
        try
        {
            channelId = await GetChannelIdInternalAsync(channel);

            var settings = await GetChannelSettingsAsync(channelId);
            if (!settings.AllowOffline || !settings.AllowOnline)
            {
                bool isOnline = await IsChannelOnlineAsync(channel, channelId);

                if (isOnline && !settings.AllowOnline)
                {
                    _logger.LogDebug("[TW] Bot is disabled during ONLINE for #{Channel}. Message ignored", channel);
                    return;
                }

                if (!isOnline && !settings.AllowOffline)
                {
                    _logger.LogDebug("[TW] Bot is disabled during OFFLINE for #{Channel}. Message ignored", channel);
                    return;
                }
            }

            // If this channel already has an active IRC fallback, skip the API entirely.
            if (IsIrcFallbackActive(channelId))
            {
                _logger.LogDebug("[TW] #{Channel} is in IRC fallback mode, using IRC", channel);
                SendIrcMessage(channel, sanitizedMessage);
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

            await _apiPipeline.ExecuteAsync(async ct =>
                await _api.Helix.Chat.SendChatMessage(request, accessToken: appToken));

            _logger.LogDebug("[TW] Sent message to #{Channel}: \"{Message}\"", channel, sanitizedMessage);
        }
        catch (Exception ex) when (channelId != null)
        {
            // Helix API failed - activate IRC fallback for this channel for 1 hour
            SetIrcFallback(channelId);

            try
            {
                await SendIrcMessage(channel, sanitizedMessage);
            }
            catch (Exception ircEx)
            {
                _logger.LogError(ircEx, "[TW] IRC fallback also failed for #{Channel}", channel);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TW] Failed to resolve channel ID for #{Channel}, cannot send message", channel);
            throw;
        }
    }
    
    private void SetIrcFallback(string channelId)
    {
        var expiry = DateTime.UtcNow + IrcFallbackDuration;
        _ircFallbackChannels[channelId] = expiry;
        _logger.LogWarning(
            "[TW] IRC fallback activated for channel {ChannelId}. Will expire at {Expiry:u}",
            channelId, expiry);
    }
    
    private bool IsIrcFallbackActive(string channelId)
    {
        if (!_ircFallbackChannels.TryGetValue(channelId, out var expiry))
            return false;

        if (DateTime.UtcNow < expiry)
            return true;
        
        _ircFallbackChannels.TryRemove(channelId, out _);
        _logger.LogInformation("[TW] IRC fallback for channel {ChannelId} expired, removed", channelId);
        return false;
    }
    
    public void ClearIrcFallback(string channelId)
    {
        if (_ircFallbackChannels.TryRemove(channelId, out _))
        {
            _logger.LogInformation(
                "[TW] IRC fallback cleared for channel {ChannelId}",
                channelId);
        }
    }
    
    private async Task SendIrcMessage(string channel, string message)
    {
        var normalizedChannel = channel.ToLowerInvariant();

        if (!IsJoined(normalizedChannel))
        {
            _logger.LogWarning(
                "[TW] [IRC Fallback] Bot is not joined to #{Channel}, cannot send via IRC",
                normalizedChannel);
            return;
        }

        await _ircClient.SendMessageAsync(normalizedChannel, message);
    }

    public async Task SendWhisperAsync(string recipientUserId, string message)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        try
        {
            var sanitizedMessage = SanitizeMessage(message);
            await _twitchPipeline.ExecuteAsync(async ct =>
                await _api.Helix.Whispers.SendWhisperAsync(_botId, recipientUserId, sanitizedMessage, true));

            _logger.LogDebug("[TW] Sent whisper to {Recipient}: \"{Message}\"", recipientUserId, sanitizedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TW] Failed to send whisper to {Recipient}", recipientUserId);
            throw;
        }
    }

    private async Task<List<string>> ReconnectToChannelsAsync()
    {
        List<string> missingChannels = [.. _initialChannels
            .Where(channel => !_ircClient.JoinedChannels.Any(jc => jc.Channel == channel))];

        foreach (var c in missingChannels)
        {
            await _ircClient.JoinChannelAsync(c, true);
        }

        return missingChannels;
    }

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

    public async Task<string?> GetChannelIdAsync(string channelName)
    {
        try
        {
            var normalized = channelName.ToLowerInvariant();
            if (_channelIdCache.TryGetValue(normalized, out var cachedId))
            {
                return cachedId;
            }

            var channelUser = await _twitchPipeline.ExecuteAsync(async ct =>
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
            _logger.LogWarning(ex, "[TW] Failed to get channel ID for {Channel}", channelName);
            return null;
        }
    }

    private async Task<string> GetChannelIdInternalAsync(string channelName)
    {
        var normalized = channelName.ToLowerInvariant();
        if (_channelIdCache.TryGetValue(normalized, out var cachedId))
        {
            return cachedId;
        }

        var channelUser = await _twitchPipeline.ExecuteAsync(async ct =>
            (await _api.Helix.Users.GetUsersAsync(logins: [normalized])).Users.FirstOrDefault());

        if (channelUser == null)
        {
            throw new InvalidOperationException($"Channel {normalized} not found");
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
            _logger.LogWarning(ex, "[TW] Failed to load settings from Redis for channel {ChannelId}", channelId);
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
            var res = await _twitchPipeline.ExecuteAsync(async ct =>
                (await _api.Helix.Streams.GetStreamsAsync(userIds: new List<string> { channelId })));
            bool isOnline = res.Streams != null && res.Streams.Length > 0;

            _streamStatusCache[channelId] = new StreamStatusInfo
            {
                IsOnline = isOnline,
                LastChecked = DateTime.UtcNow
            };

            return isOnline;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TW] Error checking stream status for {Channel}", channelLogin);
            return false;
        }
    }
    
    public void InvalidateChannelSettingsCache(string channelId) => _settingsCache.TryRemove(channelId, out _);
    
    private async Task<string?> GetAppAccessTokenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.ClientSecret) || string.IsNullOrWhiteSpace(_config.ClientId))
        {
            _logger.LogInformation("[TW] App Access Token requested but ClientId/ClientSecret not configured, falling back to bot token");
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
            // Double-check after acquiring lock
            if (_appTokenCache.TryGetValue(AppTokenCacheKey, out cached)
                && cached.ExpiresAt - DateTime.UtcNow > TokenExpiryBuffer)
            {
                return cached.Token;
            }

            var entry = await FetchAppAccessTokenAsync(ct);
            _appTokenCache[AppTokenCacheKey] = entry;
            _logger.LogInformation("[TW] App Access Token refreshed successfully. Expires at {ExpiresAt:u}", entry.ExpiresAt);
            return entry.Token;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "[TW] App Access Token receive error");
            throw;
        }
        finally
        {
            _appTokenRefreshLock.Release();
        }
    }

    private async Task<AppAccessTokenEntry> FetchAppAccessTokenAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("[TW] Fetching new App Access Token");
        using var content = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("client_id", _config.ClientId),
            new KeyValuePair<string, string>("client_secret", _config.ClientSecret),
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        ]);

        using var response = await _tokenHttpClient.PostAsync("https://id.twitch.tv/oauth2/token", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Twitch token endpoint returned HTTP {(int)response.StatusCode}. Body: {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var tokenResponse = JsonSerializer.Deserialize<AppTokenResponse>(json)
            ?? throw new InvalidOperationException("App Access Token response was null");

        if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("App Access Token response contained an empty access_token field");
        }

        return new AppAccessTokenEntry(
            Token: tokenResponse.AccessToken,
            ExpiresAt: DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn));
    }

    public void SetBroadcasterToken(string channelId, string token)
    {
        _broadcasterTokens[channelId] = token;
    }

    public string? GetBroadcasterToken(string channelId)
    {
        return _broadcasterTokens.TryGetValue(channelId, out var token) ? token : null;
    }

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
    }

    private async Task OnClientReconnected(object? sender, OnConnectedEventArgs e)
    {
        try
        {
            _logger.LogInformation("[TW] Reconnected");
            await ReconnectToChannelsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TW] Error handling reconnect");
        }
    }

    private Task OnClientMessageReceived(object? sender, TwitchLib.Client.Events.OnMessageReceivedArgs e)
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
            _logger.LogError(ex, "[TW] Error handling Twitch message");
        }

        return Task.CompletedTask;
    }

    private async Task OnClientConnected(object? sender, OnConnectedEventArgs e)
    {
        try
        {
            _logger.LogInformation("[TW] Connected");
            OnConnected?.Invoke(this, e);
            await ReconnectToChannelsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TW] Error handling");
        }
    }

    private Task OnWhisperMessage(object? sender, TwitchLib.EventSub.Core.EventArgs.User.UserWhisperMessageArgs e)
    {
        try
        {
            var data = e.Payload.Event;
            var message = data.Whisper.Text.Trim();

            // S0: Try to decode Base64 JSON from the website
            string json;
            try
            {
                var bytes = Convert.FromBase64String(message);
                json = System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                _logger.LogDebug("[TW] Whisper message is not a valid Base64 string, ignoring");
                return Task.CompletedTask;
            }

            // S1: Parse JSON to extract channel and token
            var authData = JsonSerializer.Deserialize<BroadcasterAuthPayload>(json);
            if (authData == null || string.IsNullOrWhiteSpace(authData.Channel) || string.IsNullOrWhiteSpace(authData.Token))
            {
                _logger.LogWarning("[TW] Invalid auth payload format in whisper from {User}", data.FromUserName);
                return Task.CompletedTask;
            }

            // S2: Trigger the auth event
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
            _logger.LogError(ex, "[TW] Error handling whisper");
        }

        return Task.CompletedTask;
    }

    private Task OnClientDisconnected(object? sender, OnDisconnectedArgs e)
    {
        _logger.LogWarning("[TW] Disconnected");
        return Task.CompletedTask;
    }

    private Task OnClientConnectionError(object? sender, OnConnectionErrorArgs e)
    {
        _logger.LogError("[TW] Connection error: {Error}", e.Error);
        
        return Task.CompletedTask;
    }

    private Task OnClientJoinedChannel(object? sender, OnJoinedChannelArgs e)
    {
        var channel = e.Channel.ToLowerInvariant();
        _logger.LogDebug("[TW] Joined #{Channel}", channel);
        
        return Task.CompletedTask;
    }

    private Task OnClientPartChannel(object? sender, OnLeftChannelArgs e)
    {
        var channel = e.Channel.ToLowerInvariant();
        _logger.LogDebug("[TW] Parted #{Channel}", channel);
        
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
            _logger.LogError(ex, "[TW] Error handling new subscriber event");
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
            _logger.LogError(ex, "[TW] Error handling gifted subscription event");
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
                ViewerCount = Int32.Parse(e.RaidNotification.MsgParamViewerCount)
            };
            OnRaidNotification?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TW] Error handling raid notification event");
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
            _logger.LogError(ex, "[TW] Error handling bits received event");
        }
        
        return Task.CompletedTask;
    }

    private async Task OnEventSubReconnected(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketReconnectedArgs e)
    {
        _logger.LogInformation("[TW] [EventSub] Reconnected!");

        await SubscribeToWhispersAsync();
    }

    private async Task OnEventSubConnected(object? sender, TwitchLib.EventSub.Websockets.Core.EventArgs.WebsocketConnectedArgs e)
    {
        _logger.LogInformation("[TW] [EventSub] Connected!");

        await SubscribeToWhispersAsync();
    }

    private string SanitizeMessage(string message)
    {
        return Regex.Replace(message, @"\s+", " ")
            .Replace("\r", "")
            .Replace("\n", " ")
            .Trim();
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

            if (_eventSubClient != null)
            {
                await _eventSubClient.DisconnectAsync();
            }

            _logger.LogInformation("[TW] Disconnected successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TW] Error during disconnection");
        }
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
            _isDisposed = true;
            _appTokenRefreshLock.Dispose();
            _tokenHttpClient.Dispose();
            if (_ircClient.IsConnected)
            {
                _ = _ircClient.DisconnectAsync();
            }
            _logger.LogInformation("[TW] Client disposed successfully");
        }
        _isDisposed = true;
    }

    ~TwitchClient()
    {
        Dispose(false);
    }

    private sealed class AppTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }

    private sealed class BroadcasterAuthPayload
    {
        [JsonPropertyName("channel")]
        public string Channel { get; set; } = string.Empty;

        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;
    }
}