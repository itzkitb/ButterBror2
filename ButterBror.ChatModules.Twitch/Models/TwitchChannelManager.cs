using System.Text.Json;
using ButterBror.Data;
using ButterBror.Data.Interfaces;

namespace ButterBror.ChatModules.Twitch.Models;

public class TwitchChannelManager(ICustomDataRepository db) : ITwitchChannelManager
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const string RedisKey = "twitch:channels";

    public async Task<List<TwitchManagedChannel>> GetChannelsAsync()
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

    public async Task AddChannelAsync(TwitchManagedChannel channel)
    {
        await _lock.WaitAsync();
        try
        {
            var channels = await GetChannelsInternalAsync();
            if (!channels.Any(item => item.Id.Equals(channel.Id, StringComparison.OrdinalIgnoreCase)))
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

    public async Task RemoveChannelAsync(string channelId)
    {
        await _lock.WaitAsync();
        try
        {
            var channels = await GetChannelsInternalAsync();
            if (channels.RemoveAll(c => string.Equals(c.Id, channelId, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                await db.SetDataAsync(RedisKey, JsonSerializer.Serialize(channels));
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    private async Task<List<TwitchManagedChannel>> GetChannelsInternalAsync()
    {
        var json = await db.GetDataAsync(RedisKey) ?? "[]";
        try
        {
            return JsonSerializer.Deserialize<List<TwitchManagedChannel>>(json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}