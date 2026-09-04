using ButterBror.ChatModules.Twitch.Interfaces;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Scopes;
using ButterBror.Data.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using ButterBror.ChatModules.Twitch.Events;
using TwitchLib.Api;
using OnMessageReceivedArgs = ButterBror.ChatModules.Twitch.Events.OnMessageReceivedArgs;

namespace ButterBror.ChatModules.Twitch.Services;

public sealed class TwitchClient : ITwitchClient, IDisposable
{
    // ><> constants & static fields
    private const int NormalChannelDelayMs = 1500;
    private const int ModVipChannelDelayMs = 100;
    private const string WhisperKeyPrefix = "whisper:";
    
    // ><> dependencies & configuration
    private readonly ILogger<TwitchClient> _logger;
    private readonly ResiliencePipeline _twitchPipeline;
    private readonly ICustomDataRepository _db;
    private readonly ITwitchChatTransport _chatTransport;
    private readonly ITwitchTokenManager _tokenManager;
    private readonly HttpClient _tokenHttpClient = new();
    private readonly TwitchAPI _api;

    // ><> collections & caches
    private readonly HashSet<TwitchManagedChannel> _initialChannels;
    private readonly ConcurrentDictionary<string, string> _channelIdCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _broadcasterTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TwitchChannelSettings> _settingsCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StreamStatusInfo> _streamStatusCache = new(StringComparer.Ordinal);
    private readonly TimeSpan _statusCacheDuration = TimeSpan.FromMinutes(2);
    
    // ><> rate limit & queues
    private readonly ConcurrentDictionary<string, bool> _isModOrVipCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Channel<QueuedMessage>>> _channelQueues = new(StringComparer.OrdinalIgnoreCase);
    
    // ><> locks & state
    private readonly CancellationTokenSource _cts = new();
    private string _botId = string.Empty;
    private string _clientId = string.Empty;
    private bool _isDisposed;
    private bool _isDisconnecting;

    private sealed record QueuedMessage(
        string Channel,
        string Message,
        string? ReplyToMessageId,
        TaskCompletionSource TaskCompletionSource
    );
    
    #region ><> Events
    public event EventHandler<OnMessageReceivedArgs>? OnMessageReceived;
    public event EventHandler<OnConnectedArgs>? OnConnected;
    public event EventHandler<OnDisconnectedArgs>? OnDisconnected;
    #endregion
    
    // ><> properties
    public bool IsConnected => _chatTransport.IsConnected;
    public IReadOnlyCollection<string> ConnectedChannels => _chatTransport.ConnectedChannels;
    
    // ><> constructor & finalizer
    private TwitchClient(
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<TwitchClient> logger,
        IEnumerable<TwitchManagedChannel> channels,
        ICustomDataRepository db,
        ITwitchChatTransport chatTransport,
        ITwitchTokenManager tokenManager)
    {
        _logger = logger;
        _twitchPipeline = pipelineProvider.GetPipeline("platform");
        _initialChannels = channels.ToHashSet();
        _db = db;
        _chatTransport = chatTransport;
        _tokenManager = tokenManager;
        _api = new TwitchAPI();

        _chatTransport.MessageReceived += OnTransportMessageReceived;
        _chatTransport.UserStateChanged += OnTransportUserStateChanged;
    }

    public static async Task<TwitchClient> CreateAsync(
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<TwitchClient> logger,
        IEnumerable<TwitchManagedChannel> channels,
        ICustomDataRepository db,
        ITwitchChatTransport chatTransport,
        ITwitchTokenManager tokenManager)
    {
        await using (new InitializationScope(logger, "twitch client"))
        {
            return new TwitchClient(pipelineProvider, logger, channels, db, chatTransport, tokenManager);
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
                _clientId = clientId;
                _api.Settings.ClientId = _clientId;
                _api.Settings.AccessToken = await _tokenManager.GetAppAccessTokenAsync();

                _botId = await GetChannelIdAsync(username) ??
                         throw new InvalidOperationException("failed to retrieve bot user id");

                _logger.LogInformation("[tw] api initialized. botId={Id}, name={Name}", _botId, username);

                await _chatTransport.ConnectAsync();
                
                foreach (var channel in _initialChannels.ToArray())
                {
                    var managedChannel = await ResolveChannelAsync(channel.Id);
                    if (managedChannel is null)
                    {
                        _logger.LogWarning("[tw] failed to resolve startup channel: {Channel}", channel);
                        continue;
                    }

                    _initialChannels.Remove(channel);
                    _initialChannels.Add(managedChannel);
                    await _chatTransport.JoinChannelAsync(managedChannel.Login);
                }
                
                OnConnected?.Invoke(this, new OnConnectedArgs());
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
        if (_isDisposed || _isDisconnecting)
            return;
            
        _isDisconnecting = true;
        
        try
        {
            await _chatTransport.DisconnectAsync();
            OnDisconnected?.Invoke(this, new OnDisconnectedArgs());
            _logger.LogInformation("[tw] disconnected successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw] error during disconnection");
        }
        finally
        {
            _isDisconnecting = false;
        }
    }
    
    // ><> public api - channels
    public async Task AddChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        
        var managedChannel = await ResolveChannelAsync(channel);
        if (managedChannel is null)
            throw new InvalidOperationException($"Channel '{channel}' not found");
        
        if (!_initialChannels.Add(managedChannel)) 
            return;

        if (_chatTransport.IsConnected)
            await _chatTransport.JoinChannelAsync(managedChannel.Login);
    }

    public async Task<bool> TryRemoveChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var normalizedChannel = channel.ToLowerInvariant();
        var managedChannel = await ResolveChannelAsync(channel);
        if (managedChannel != null && !_initialChannels.Contains(managedChannel))
            return true;

        if (_chatTransport.IsConnected && IsJoined(normalizedChannel))
            await _chatTransport.LeaveChannelAsync(normalizedChannel);
        
        return managedChannel != null && _initialChannels.Remove(managedChannel);
    }

    public async Task JoinChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!_chatTransport.IsConnected)
            throw new InvalidOperationException("Twitch transport is not connected yet");

        var managedChannel = await ResolveChannelAsync(channel);
        if (managedChannel is null)
            throw new InvalidOperationException($"Channel '{channel}' not found");

        var normalizedChannel = managedChannel.Login.ToLowerInvariant();
        if (IsJoined(normalizedChannel))
            return;

        _logger.LogInformation("[tw] joining #{Channel}", normalizedChannel);
        await _chatTransport.JoinChannelAsync(normalizedChannel);
    }

    public async Task JoinBotChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var managedChannel = await ResolveChannelAsync(channel);
        if (managedChannel is null)
            throw new InvalidOperationException($"channel #{channel} not found");

        await _chatTransport.JoinChannelViaIrcAsync(managedChannel.Login);
    }

    public async Task LeaveChannelAsync(string channel)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var normalizedChannel = channel.ToLowerInvariant();
        
        if (!IsJoined(normalizedChannel))
            return;
        
        _logger.LogInformation("[tw] parting #{Channel}", normalizedChannel);
        
        if (_channelQueues.TryRemove(normalizedChannel, out var lazyQueue))
        {
            lazyQueue.Value.Writer.TryComplete();
        }
        
        await _chatTransport.LeaveChannelAsync(normalizedChannel);
    }

    public bool IsJoined(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return false;
        var normalizedChannel = channel.ToLowerInvariant();
        
        return _chatTransport.ConnectedChannels.Any(c => 
            c.Equals(normalizedChannel, StringComparison.OrdinalIgnoreCase));
    }
    
    // ><> public api - messaging
    public async Task SendMessageAsync(string channel, string message, bool convertChannelId = true)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await EnqueueMessageAsync(channel, message, replyToMessageId: null);
    }

    public async Task SendReplyAsync(string channel, string replyToMessageId, string message, bool convertChannelId = true)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (string.IsNullOrWhiteSpace(replyToMessageId))
        {
            _logger.LogWarning("[tw] sendreply called with empty messageid, falling back to sendmessage");
            await SendMessageAsync(channel, message, convertChannelId);
            return;
        }
        await EnqueueMessageAsync(channel, message, replyToMessageId);
    }

    private async Task EnqueueMessageAsync(string channel, string message, string? replyToMessageId)
    {
        var normalizedChannel = channel.ToLowerInvariant();

        // whisper routing
        if (normalizedChannel.StartsWith(WhisperKeyPrefix))
        {
            var userId = normalizedChannel[WhisperKeyPrefix.Length..];
            await SendWhisperAsync(userId, message);
            return; 
        }
        
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new QueuedMessage(normalizedChannel, message, replyToMessageId, tcs);
        
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
        
        await queue.Value.Writer.WriteAsync(item, _cts.Token);
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

                    await SendHelixMessageAsync(item.Channel, item.Message, item.ReplyToMessageId);
                    item.TaskCompletionSource.TrySetResult();
                }
                catch (OperationCanceledException)
                {
                    item.TaskCompletionSource.TrySetCanceled();
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[tw] error processing queued message for #{Channel}", channel);
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
            _api.Settings.ClientId = _clientId;
            _api.Settings.AccessToken = await _tokenManager.GetUserAccessTokenAsync();
            
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
    
    // ><> public api - tokens & resolution
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
            var channel = await ResolveChannelAsync(channelName).ConfigureAwait(false);
            return channel?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[tw] failed to get channel id for #{Channel}", channelName);
            return null;
        }
    }

    public async Task<TwitchManagedChannel?> ResolveChannelAsync(string channelOrId, CancellationToken cancellationToken = default)
    {
        var normalized = channelOrId.TrimStart('#').TrimStart('@').ToLowerInvariant();
        return await ResolveChannelWithTokenAsync(normalized, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TwitchManagedChannel?> ValidateBotTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;
    
        try
        {
            _api.Settings.ClientId = _clientId;
            _api.Settings.AccessToken = accessToken;
            var response = await _api.Helix.Users.GetUsersAsync().ConfigureAwait(false);
            var user = response.Users.FirstOrDefault();
            return user is null ? null : new TwitchManagedChannel(user.Id, user.Login, user.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[tw] failed to validate bot token");
            return null;
        }
    }

    private async Task<TwitchManagedChannel?> ResolveChannelWithTokenAsync(string? channelOrId, string? accessToken, CancellationToken cancellationToken)
    {
        var normalized = channelOrId?.TrimStart('#').TrimStart('@').ToLowerInvariant() ?? string.Empty;
        
        _api.Settings.ClientId = _clientId;
        _api.Settings.AccessToken = string.IsNullOrWhiteSpace(accessToken)
            ? await _tokenManager.GetAppAccessTokenAsync(cancellationToken)
            : accessToken;
        
        var user = normalized.All(char.IsDigit)
            ? (await _api.Helix.Users.GetUsersAsync(ids: [normalized]).ConfigureAwait(false)).Users.FirstOrDefault()
            : (await _api.Helix.Users.GetUsersAsync(logins: [normalized]).ConfigureAwait(false)).Users.FirstOrDefault();
        
        if (user is null)
            return null;
        
        _channelIdCache[user.Id] = user.Login;
        return new TwitchManagedChannel(user.Id, user.Login, user.DisplayName);
    }

    public async Task UpgradeToEventSubAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var channel = await ResolveChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (channel is not null)
            await _chatTransport.RefreshChannelAsync(channel.Login, cancellationToken).ConfigureAwait(false);
    }

    public void InvalidateChannelSettingsCache(string channelId) => _settingsCache.TryRemove(channelId, out _);

    public Task RefreshChannelAsync(string channelId)
    {
        var channel = _channelIdCache.FirstOrDefault(pair => pair.Value.Equals(channelId, StringComparison.OrdinalIgnoreCase)).Key;
        return string.IsNullOrWhiteSpace(channel)
            ? Task.CompletedTask
            : _chatTransport.RefreshChannelAsync(channel);
    }
    
    // ><> messaging & fallbacks helpers
    private async Task SendHelixMessageAsync(string channel, string message, string? replyToMessageId)
    {
        var sanitizedMessage = SanitizeMessage(message);
        if (sanitizedMessage.Length > 500)
            sanitizedMessage = sanitizedMessage[..497] + "...";

        try
        {
            var resolvedChannel = await ResolveChannelAsync(channel, _cts.Token).ConfigureAwait(false);
            if (resolvedChannel is null)
                throw new InvalidOperationException($"Channel #{channel} not found");

            var channelId = resolvedChannel.Id;
            var channelLogin = resolvedChannel.Login;

            var settings = await GetChannelSettingsAsync(channelId);
            if (!settings.AllowOffline || !settings.AllowOnline)
            {
                var isOnline = await IsChannelOnlineAsync(channelLogin, channelId);

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
            
            await _chatTransport.SendMessageAsync(channelLogin, sanitizedMessage, replyToMessageId);
            _logger.LogDebug("[tw] sent message to #{Channel}: \"{Message}\"", channel, sanitizedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw] failed to resolve or send message to #{Channel}", channel);
            throw;
        }
    }

    private static string SanitizeMessage(string message)
    {
        return Regex.Replace(message, @"\s+", " ")
            .Replace("\r", "")
            .Replace("\n", " ")
            .Trim();
    }
    
    // ><> channels & settings helpers
    private void OnTransportUserStateChanged(object? sender, OnUserStateChangedArgs e)
    {
        try
        {
            _isModOrVipCache[e.Channel] = e.IsModerator || e.IsVip;
            _logger.LogDebug("[tw] User state updated for #{Channel}: Mod={IsMod}, Vip={IsVip}", 
                e.Channel, e.IsModerator, e.IsVip);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw] error updating user state cache for #{Channel}", e.Channel);
        }
    }
    
    private void OnTransportMessageReceived(object? sender, OnMessageReceivedArgs e)
    {
        OnMessageReceived?.Invoke(this, e);
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
            _logger.LogWarning(ex, "[tw] failed to load settings from db for #{ChannelId}", channelId);
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
            _api.Settings.ClientId = _clientId;
            _api.Settings.AccessToken = await _tokenManager.GetAppAccessTokenAsync();
            
            var res = await _twitchPipeline.ExecuteAsync(async _ =>
                await _api.Helix.Streams.GetStreamsAsync(userIds: [channelId]));
            
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
            
            _tokenHttpClient.Dispose();
            _logger.LogInformation("[tw] client disposed successfully");
        }
    }
}