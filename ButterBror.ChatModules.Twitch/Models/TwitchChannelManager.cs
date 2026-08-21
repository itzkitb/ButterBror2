using System.Text.Json;
using ButterBror.Data;
using ButterBror.Data.Interfaces;

namespace ButterBror.ChatModules.Twitch.Models;

public class TwitchChannelManager(ICustomDataRepository db) : ITwitchChannelManager
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const string RedisKey = "twitch:channels";

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
                await db.SetDataAsync(RedisKey, JsonSerializer.Serialize(channels));
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
                await db.SetDataAsync(RedisKey, JsonSerializer.Serialize(channels));
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    private async Task<List<string>> GetChannelsInternalAsync()
    {
        var json = await db.GetDataAsync(RedisKey) ?? "[]";
        return JsonSerializer.Deserialize<List<string>>(json) ?? new();
    }
}