using System.Text.Json;
using ButterBror.Data;

namespace ButterBror.ChatModules.Twitch.Models;

public class TwitchChannelManager : ITwitchChannelManager
{
    private readonly ICustomDataRepository _db;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const string RedisKey = "twitch:channels";

    public TwitchChannelManager(ICustomDataRepository db)
    {
        _db = db;
    }

    public async Task<List<string>> GetChannelsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await GetChannelsInternalAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddChannelAsync(string channel)
    {
        await _lock.WaitAsync();
        try
        {
            var channels = await GetChannelsInternalAsync();
            if (!channels.Contains(channel, StringComparer.OrdinalIgnoreCase))
            {
                channels.Add(channel);
                await _db.SetDataAsync(RedisKey, JsonSerializer.Serialize(channels));
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveChannelAsync(string channel)
    {
        await _lock.WaitAsync();
        try
        {
            var channels = await GetChannelsInternalAsync();
            if (channels.RemoveAll(c => string.Equals(c, channel, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                await _db.SetDataAsync(RedisKey, JsonSerializer.Serialize(channels));
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    private async Task<List<string>> GetChannelsInternalAsync()
    {
        var json = await _db.GetDataAsync(RedisKey) ?? "[]";
        return JsonSerializer.Deserialize<List<string>>(json) ?? new();
    }
}