using System.Diagnostics;
using System.Text.Json;
using ButterBror.ChatModules.Twitch.Events;
using ButterBror.ChatModules.Twitch.Interfaces;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Data.Interfaces;
using Microsoft.Extensions.Logging;

namespace ButterBror.ChatModules.Twitch.Services;

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
}