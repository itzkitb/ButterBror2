using ButterBror.Data.Interfaces;
using Polly;
using Polly.Registry;
using StackExchange.Redis;

namespace ButterBror.Data.Repositories;

public class RedisCustomDataRepository(
    IConnectionMultiplexer redis,
    ResiliencePipelineProvider<string> pipelineProvider)
    : ICustomDataRepository
{
    private readonly ResiliencePipeline _redisPipeline = pipelineProvider.GetPipeline("redis");
    private const string CustomPrefix = "custom:";

    public async Task SetDataAsync(string key, string value, TimeSpan? expiry = null)
    {
        await _redisPipeline.ExecuteAsync(async _ => 
        {
            var db = redis.GetDatabase();
            if (expiry != null)
            {
                await db.StringSetAsync($"{CustomPrefix}{key}", value, (TimeSpan)expiry);
            }
            else
            {
                await db.StringSetAsync($"{CustomPrefix}{key}", value);
            }
        });
    }

    public async Task<string?> GetDataAsync(string key)
    {
        return await _redisPipeline.ExecuteAsync(async _ => 
        {
            var db = redis.GetDatabase();
            var value = await db.StringGetAsync($"{CustomPrefix}{key}");
            return value.HasValue ? value.ToString() : null;
        });
    }

    public async Task<bool> DeleteDataAsync(string key)
    {
        return await _redisPipeline.ExecuteAsync(async _ => 
        {
            var db = redis.GetDatabase();
            return await db.KeyDeleteAsync($"{CustomPrefix}{key}");
        });
    }

    public async Task<IReadOnlyDictionary<string, string>> ScanAsync(string pattern)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            var result = new Dictionary<string, string>();
 
            var server = redis.GetServer(redis.GetEndPoints().First());
            var db = redis.GetDatabase();
 
            var fullPattern = $"{CustomPrefix}{pattern}";
 
            await foreach (var redisKey in server.KeysAsync(pattern: fullPattern).WithCancellation(ct))
            {
                var val = await db.StringGetAsync(redisKey);
                if (!val.HasValue) continue;
 
                var userKey = redisKey.ToString()[CustomPrefix.Length..];
                result[userKey] = val.ToString();
            }
 
            return (IReadOnlyDictionary<string, string>)result;
        });
    }
}
