using ButterBror.Core.Models;
using ButterBror.Data.Interfaces;
using Polly;
using Polly.Registry;
using StackExchange.Redis;

namespace ButterBror.Data.Repositories;

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

    public async Task<IReadOnlyList<BanphraseRecord>> GetGlobalCategoriesAsync()
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var members = await db.SetMembersAsync(GlobalSetKey);
            
            if (members.Length == 0)
                return [];
            
            var batch = db.CreateBatch();
            var tasks = new List<Task<RedisValue>>(members.Length);
            tasks.AddRange(members.Select(member => batch.StringGetAsync($"{GlobalPrefix}{member}")));

            batch.Execute();
            
            var patterns = await Task.WhenAll(tasks);
            var result = new List<BanphraseRecord>(members.Length);
        
            for (var i = 0; i < members.Length; i++)
            {
                if (patterns[i].IsNullOrEmpty)
                    continue;
                
                result.Add(new BanphraseRecord(members[i].ToString(), patterns[i].ToString()));
            }

            return result;
        });
    }

    public async Task<BanphraseRecord?> GetGlobalCategoryAsync(string categoryName)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var value = await db.StringGetAsync($"{GlobalPrefix}{categoryName}");
            
            return value.HasValue ? new BanphraseRecord(categoryName, value.ToString()) : null;
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

    public async Task<IReadOnlyList<BanphraseRecord>> GetChannelCategoriesAsync(Guid chatId)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var members = await db.SetMembersAsync($"{ChannelSetKeyPrefix}{chatId}:categories");
            
            if (members.Length == 0)
                return [];
            
            var batch = db.CreateBatch();
            var tasks = new List<Task<RedisValue>>(members.Length);

            foreach (var member in members)
            {
                tasks.Add(batch.StringGetAsync($"{ChannelPrefix}{chatId}:{member}"));
            }
            
            batch.Execute();
            
            var patterns = await Task.WhenAll(tasks);
            var result = new List<BanphraseRecord>(members.Length);
        
            for (var i = 0; i < members.Length; i++)
            {
                if (patterns[i].IsNullOrEmpty)
                    continue;
                
                result.Add(new BanphraseRecord(members[i].ToString(), patterns[i].ToString()));
            }

            return result;
        });
    }

    public async Task<BanphraseRecord?> GetChannelCategoryAsync(Guid chatId, string categoryName)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var key = $"{ChannelPrefix}{chatId}:{categoryName}";
            var value = await db.StringGetAsync(key);
            return value.HasValue ? new BanphraseRecord(categoryName, value.ToString()) : null;
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
}