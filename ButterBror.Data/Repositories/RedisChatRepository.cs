using System.Text;
using System.Text.Json;
using ButterBror.Data.Interfaces;
using ButterBror.Domain.Entities;
using Polly;
using Polly.Registry;
using StackExchange.Redis;

namespace ButterBror.Data.Repositories;

public class RedisChatRepository(
    IConnectionMultiplexer redis,
    ResiliencePipelineProvider<string> pipelineProvider)
    : IChatRepository
{
    private readonly ResiliencePipeline _redisPipeline = pipelineProvider.GetPipeline("redis");
    private const string ChatPrefix = "chat:";
    private const string PlatformIndexPrefix = "index:chat_platform:";
    private const string TitleIndexPrefix = "index:chat_title:";

    public async Task<ChatInfo?> GetByUnifiedIdAsync(Guid unifiedId)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            var db = redis.GetDatabase();
            var key = $"{ChatPrefix}{unifiedId}";
            var json = await db.StringGetAsync(key).WaitAsync(ct);
            return json.HasValue ? JsonSerializer.Deserialize<ChatInfo>(json.ToString()) : null;
        });
    }

    public async Task<ChatInfo?> GetByPlatformIdAsync(string platform, string platformId)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var indexKey = $"{PlatformIndexPrefix}{platform.ToLowerInvariant()}:{platformId}";
            var unifiedId = await db.StringGetAsync(indexKey);
            return unifiedId.HasValue ? await GetByUnifiedIdAsync(Guid.Parse(unifiedId.ToString())) : null;
        });
    }

    public async Task<ChatInfo?> GetByTitleAsync(string platform, string title)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var chatTitleB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(title));
            var db = redis.GetDatabase();
            var indexKey = $"{TitleIndexPrefix}{platform.ToLowerInvariant()}:{chatTitleB64}";
            var unifiedId = await db.StringGetAsync(indexKey);
            return unifiedId.HasValue ? await GetByUnifiedIdAsync(Guid.Parse(unifiedId.ToString())) : null;
        });
    }

    public async Task<ChatInfo> CreateOrUpdateAsync(ChatInfo chat)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var key = $"{ChatPrefix}{chat.UnifiedId}";
            var json = JsonSerializer.Serialize(chat);

            await db.StringSetAsync(key, json);

            // updating platform index
            var chatTitleB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(chat.Title));
            var platformIndexKey = $"{PlatformIndexPrefix}{chat.Platform}:{chat.PlatformId}";
            var platformTitleKey = $"{TitleIndexPrefix}{chat.Platform}:{chatTitleB64}";
            
            await db.StringSetAsync(platformIndexKey, chat.UnifiedId.ToString());
            await db.StringSetAsync(platformTitleKey, chat.UnifiedId.ToString());
            
            return chat;
        });
    }

    public async Task<bool> ChatExistsAsync(Guid unifiedId)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var key = $"{ChatPrefix}{unifiedId}";
            return await db.KeyExistsAsync(key);
        });
    }
}