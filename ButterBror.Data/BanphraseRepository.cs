using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;
using StackExchange.Redis;

namespace ButterBror.Data;

public class BanphraseRepository(
    IConnectionMultiplexer redis,
    ResiliencePipelineProvider<string> pipelineProvider)
    : IBanphraseRepository
{
    private readonly ResiliencePipeline _redisPipeline = pipelineProvider.GetPipeline("redis");
    
    private const string GlobalPrefix = "banphrases:global:";
    private const string GlobalSetKey = "banphrases:global:categories";
    private const string ChannelPrefix = "banphrases:";
    private const string ChannelSetKeyPrefix = "banphrases:channels:";

    public async Task<IReadOnlyList<string>> GetGlobalCategoryNamesAsync()
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            IDatabase db = redis.GetDatabase();
            var members = await db.SetMembersAsync(GlobalSetKey);
            return members.Select(m => m.ToString()).ToList().AsReadOnly();
        });
    }

    public async Task<string?> GetGlobalCategoryAsync(string categoryName)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var value = await db.StringGetAsync($"{GlobalPrefix}{categoryName}");
            return value.HasValue ? value.ToString() : null;
        });
    }

    public async Task SetGlobalCategoryAsync(string categoryName, string regexPattern)
    {
        await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            await db.StringSetAsync($"{GlobalPrefix}{categoryName}", regexPattern);
            await db.SetAddAsync(GlobalSetKey, categoryName);
        });
    }

    public async Task DeleteGlobalCategoryAsync(string categoryName)
    {
        await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            await db.KeyDeleteAsync($"{GlobalPrefix}{categoryName}");
            await db.SetRemoveAsync(GlobalSetKey, categoryName);
        });
    }

    public async Task<IReadOnlyList<string>> GetChannelCategoryNamesAsync(Guid chatId)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var setKey = $"{ChannelSetKeyPrefix}{chatId}:categories";
            var members = await db.SetMembersAsync(setKey);
            return members.Select(m => m.ToString()).ToList().AsReadOnly();
        });
    }

    public async Task<string?> GetChannelCategoryAsync(Guid chatId, string categoryName)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var key = $"{ChannelPrefix}{chatId}:{categoryName}";
            var value = await db.StringGetAsync(key);
            return value.HasValue ? value.ToString() : null;
        });
    }

    public async Task SetChannelCategoryAsync(Guid chatId, string categoryName, string regexPattern)
    {
        await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var key = $"{ChannelPrefix}{chatId}:{categoryName}";
            var setKey = $"{ChannelSetKeyPrefix}{chatId}:categories";
            await db.StringSetAsync(key, regexPattern);
            await db.SetAddAsync(setKey, categoryName);
        });
    }

    public async Task DeleteChannelCategoryAsync(Guid chatId, string categoryName)
    {
        await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var key = $"{ChannelPrefix}{chatId}:{categoryName}";
            var setKey = $"{ChannelSetKeyPrefix}{chatId}:categories";
            await db.KeyDeleteAsync(key);
            await db.SetRemoveAsync(setKey, categoryName);
        });
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllGlobalCategoriesAsync()
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
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

    public async Task<IReadOnlyDictionary<string, string>> GetAllChannelCategoriesAsync(Guid chatId)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var categoryNames = await GetChannelCategoryNamesAsync(chatId);
            var result = new Dictionary<string, string>();
            
            foreach (var categoryName in categoryNames)
            {
                var pattern = await GetChannelCategoryAsync(chatId, categoryName);
                if (!string.IsNullOrEmpty(pattern))
                {
                    result[categoryName] = pattern;
                }
            }
            
            return result;
        });
    }
}