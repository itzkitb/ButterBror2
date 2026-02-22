using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;
using StackExchange.Redis;

namespace ButterBror.Data;

public class BanphraseRepository : IBanphraseRepository
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ResiliencePipeline _redisPipeline;
    private readonly ILogger<BanphraseRepository> _logger;
    
    private const string GlobalPrefix = "banphrases:global:";
    private const string GlobalSetKey = "banphrases:global:categories";
    private const string ChannelPrefix = "banphrases:";
    private const string ChannelSetKeyPrefix = "banphrases:channels:";

    public BanphraseRepository(
        IConnectionMultiplexer redis,
        ILogger<BanphraseRepository> logger,
        ResiliencePipelineProvider<string> pipelineProvider)
    {
        _redis = redis;
        _logger = logger;
        _redisPipeline = pipelineProvider.GetPipeline("redis");
    }

    public async Task<IReadOnlyList<string>> GetGlobalCategoryNamesAsync()
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            var members = await db.SetMembersAsync(GlobalSetKey);
            return members.Select(m => m.ToString()).ToList().AsReadOnly();
        });
    }

    public async Task<string?> GetGlobalCategoryAsync(string categoryName)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            var value = await db.StringGetAsync($"{GlobalPrefix}{categoryName}");
            return value.HasValue ? value.ToString() : null;
        });
    }

    public async Task SetGlobalCategoryAsync(string categoryName, string regexPattern)
    {
        await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            await db.StringSetAsync($"{GlobalPrefix}{categoryName}", regexPattern);
            await db.SetAddAsync(GlobalSetKey, categoryName);
        });
    }

    public async Task DeleteGlobalCategoryAsync(string categoryName)
    {
        await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            await db.KeyDeleteAsync($"{GlobalPrefix}{categoryName}");
            await db.SetRemoveAsync(GlobalSetKey, categoryName);
        });
    }

    public async Task<IReadOnlyList<string>> GetChannelCategoryNamesAsync(string platform, string channelId)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            var setKey = $"{ChannelSetKeyPrefix}{platform}:{channelId}:categories";
            var members = await db.SetMembersAsync(setKey);
            return members.Select(m => m.ToString()).ToList().AsReadOnly();
        });
    }

    public async Task<string?> GetChannelCategoryAsync(string platform, string channelId, string categoryName)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            var key = $"{ChannelPrefix}{platform}:{channelId}:{categoryName}";
            var value = await db.StringGetAsync(key);
            return value.HasValue ? value.ToString() : null;
        });
    }

    public async Task SetChannelCategoryAsync(string platform, string channelId, string categoryName, string regexPattern)
    {
        await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            var key = $"{ChannelPrefix}{platform}:{channelId}:{categoryName}";
            var setKey = $"{ChannelSetKeyPrefix}{platform}:{channelId}:categories";
            await db.StringSetAsync(key, regexPattern);
            await db.SetAddAsync(setKey, categoryName);
        });
    }

    public async Task DeleteChannelCategoryAsync(string platform, string channelId, string categoryName)
    {
        await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            var key = $"{ChannelPrefix}{platform}:{channelId}:{categoryName}";
            var setKey = $"{ChannelSetKeyPrefix}{platform}:{channelId}:categories";
            await db.KeyDeleteAsync(key);
            await db.SetRemoveAsync(setKey, categoryName);
        });
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllGlobalCategoriesAsync()
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            var categoryNames = await GetGlobalCategoryNamesAsync();
            var result = new Dictionary<string, string>();
            
            foreach (var categoryName in categoryNames)
            {
                var pattern = await GetGlobalCategoryAsync(categoryName);
                if (!string.IsNullOrEmpty(pattern))
                {
                    result[categoryName] = pattern;
                }
            }
            
            return result;
        });
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllChannelCategoriesAsync(string platform, string channelId)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            var categoryNames = await GetChannelCategoryNamesAsync(platform, channelId);
            var result = new Dictionary<string, string>();
            
            foreach (var categoryName in categoryNames)
            {
                var pattern = await GetChannelCategoryAsync(platform, channelId, categoryName);
                if (!string.IsNullOrEmpty(pattern))
                {
                    result[categoryName] = pattern;
                }
            }
            
            return result;
        });
    }
}