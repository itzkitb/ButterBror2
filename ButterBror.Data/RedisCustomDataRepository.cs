using System;
using Polly;
using Polly.Registry;
using StackExchange.Redis;

namespace ButterBror.Data;

public class RedisCustomDataRepository : ICustomDataRepository
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ResiliencePipeline _redisPipeline;
    private const string CustomPrefix = "custom:";

    public RedisCustomDataRepository(
        IConnectionMultiplexer redis, 
        ResiliencePipelineProvider<string> pipelineProvider)
    {
        _redis = redis;
        _redisPipeline = pipelineProvider.GetPipeline("redis");
    }

    public async Task SetDataAsync(string key, string value, TimeSpan? expiry = null)
    {
        await _redisPipeline.ExecuteAsync(async ct => 
        {
            var db = _redis.GetDatabase();
            if (expiry != null)
            {
                await db.StringSetAsync($"{CustomPrefix}{key}", value, (Expiration)(TimeSpan)expiry);
            }
            else
            {
                await db.StringSetAsync($"{CustomPrefix}{key}", value);
            }
        });
    }

    public async Task<string?> GetDataAsync(string key)
    {
        return await _redisPipeline.ExecuteAsync(async ct => 
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync($"{CustomPrefix}{key}");
            return value.HasValue ? value.ToString() : null;
        });
    }

    public async Task<bool> DeleteDataAsync(string key)
    {
        return await _redisPipeline.ExecuteAsync(async ct => 
        {
            var db = _redis.GetDatabase();
            return await db.KeyDeleteAsync($"{CustomPrefix}{key}");
        });
    }

    public async Task<IReadOnlyDictionary<string, string>> ScanAsync(string pattern)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            var result = new Dictionary<string, string>();
 
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var db = _redis.GetDatabase();
 
            var fullPattern = $"{CustomPrefix}{pattern}";
 
            await foreach (var redisKey in server.KeysAsync(pattern: fullPattern))
            {
                var val = await db.StringGetAsync(redisKey);
                if (!val.HasValue) continue;
 
                var userKey = redisKey.ToString().Substring(CustomPrefix.Length);
                result[userKey] = val.ToString();
            }
 
            return (IReadOnlyDictionary<string, string>)result;
        });
    }
}
