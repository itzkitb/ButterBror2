using ButterBror.Data.Interfaces;
using Polly;
using Polly.Registry;
using StackExchange.Redis;

namespace ButterBror.Data.Repositories;

public class DataRepository(
    IConnectionMultiplexer redis,
    ResiliencePipelineProvider<string> pipelineProvider)
    : IDataRepository
{
    private readonly ResiliencePipeline _redisPipeline = pipelineProvider.GetPipeline("redis");

    public async Task SetDataAsync(string key, string value, TimeSpan? expiry = null)
    {
        await _redisPipeline.ExecuteAsync(async _ => 
        {
            var db = redis.GetDatabase();
            if (expiry != null)
            {
                await db.StringSetAsync(key, value, (TimeSpan)expiry);
            }
            else
            {
                await db.StringSetAsync(key, value);
            }
        });
    }

    public async Task<string?> GetDataAsync(string key)
    {
        return await _redisPipeline.ExecuteAsync(async _ => 
        {
            var db = redis.GetDatabase();
            var value = await db.StringGetAsync(key);
            return value.HasValue ? value.ToString() : null;
        });
    }

    public async Task<bool> DeleteDataAsync(string key)
    {
        return await _redisPipeline.ExecuteAsync(async _ => 
        {
            var db = redis.GetDatabase();
            return await db.KeyDeleteAsync(key);
        });
    }

    public async Task<IReadOnlyDictionary<string, string>> ScanAsync(string pattern)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            var result = new Dictionary<string, string>();
 
            var server = redis.GetServer(redis.GetEndPoints().First());
            var db = redis.GetDatabase();
            
            await foreach (var redisKey in server.KeysAsync(pattern: pattern).WithCancellation(ct))
            {
                var val = await db.StringGetAsync(redisKey);
                if (!val.HasValue) continue;
 
                var userKey = redisKey.ToString();
                result[userKey] = val.ToString();
            }
 
            return (IReadOnlyDictionary<string, string>)result;
        });
    }
}