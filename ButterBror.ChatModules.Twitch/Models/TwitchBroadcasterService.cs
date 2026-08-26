using System.Diagnostics;
using System.Text.Json;
using ButterBror.ChatModules.Twitch.Events;
using ButterBror.Data;
using ButterBror.Data.Interfaces;
using Microsoft.Extensions.Logging;

namespace ButterBror.ChatModules.Twitch.Models;

internal class TwitchBroadcasterService
{
    private readonly ITwitchClient _twitchClient;
    private readonly ICustomDataRepository _db;
    private readonly TwitchConfiguration _config;
    private readonly ILogger<TwitchBroadcasterService> _logger;
    private readonly ITwitchChannelManager _channelManager;
    
    public TwitchBroadcasterService(
        ITwitchClient twitchClient,
        ICustomDataRepository db,
        TwitchConfiguration config,
        ILogger<TwitchBroadcasterService> logger,
        ITwitchChannelManager channelManager)
    {
        _twitchClient = twitchClient;
        _db = db;
        _config = config;
        _logger = logger;
        _channelManager = channelManager;
    }
    
    public async Task LoadBroadcasterTokensAsync()
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
                    _logger.LogWarning(ex, "[tw] failed to load broadcaster token for #{Channel}", channel);
                }
            });
            
            timer.Stop();
            _logger.LogInformation(
                "[tw] loaded broadcaster token for {Channels} channels in {Time} ms",
                allChannels.Count,
                timer.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw] failed to load broadcaster tokens from redis");
        }
    }

    public void OnBroadcasterAuthReceived(object? sender, BroadcasterAuthReceivedArgs e)
    {
        _ = SafeHandleBroadcasterAuthAsync(e).ContinueWith(
            t => _logger.LogError(t.Exception, "[tw] unhandled exception in broadcaster auth handler"),
            TaskContinuationOptions.OnlyOnFaulted
        );
    }

    private async Task SafeHandleBroadcasterAuthAsync(BroadcasterAuthReceivedArgs e)
    {
        try
        {
            if (_twitchClient == null)
                throw new Exception("Twitch client not initialized");
            
            if (_channelManager == null)
                throw new Exception("Twitch channel manager not initialized");
            
            var channelId = await _twitchClient.GetChannelIdAsync(e.Channel);
            if (string.IsNullOrWhiteSpace(channelId))
            {
                _logger.LogWarning("[tw] channel {Channel} not found", e.Channel);
                return;
            }

            var isValid = await _twitchClient.ValidateBroadcasterTokenAsync(e.Token);
            if (!isValid)
            {
                _logger.LogWarning("[tw] invalid broadcaster token from {User} for #{Channel}", e.Username, e.Channel);
                await _twitchClient.SendMessageAsync(
                    e.Channel, 
                    "❌ | Failed to authorize. The token is invalid or expired");
                return;
            }

            var tokenKey = $"twitch:broadcaster_token:{channelId}";
            await _db.SetDataAsync(tokenKey, e.Token);
            _twitchClient.SetBroadcasterToken(channelId, e.Token);
            _twitchClient.ClearIrcFallback(channelId);
            await _twitchClient.RefreshChannelAsync(channelId);

            var managedChannel = await _twitchClient.ResolveChannelAsync(e.Channel);
            if (managedChannel is not null)
                await _channelManager.AddChannelAsync(managedChannel);
            await _twitchClient.AddChannelAsync(e.Channel);
            await _twitchClient.SendMessageAsync(e.Channel, "✅ | Successfully authorized, hi!");

            _logger.LogInformation("[tw] successfully authorized #{Channel}", e.Channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[tw] error processing broadcaster auth from {User}", e.Username);
        }
    }
}