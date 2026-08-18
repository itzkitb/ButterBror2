using System.Text.Json;
using ButterBror.Domain.Entities;
using Polly;
using Polly.Registry;
using StackExchange.Redis;

namespace ButterBror.Data;

public class RedisChatRepository(
    IConnectionMultiplexer redis,
    ResiliencePipelineProvider<string> pipelineProvider)
    : IChatRepository
{
    private readonly ResiliencePipeline _redisPipeline = pipelineProvider.GetPipeline("redis");
    private const string ChatPrefix = "chat:";
    private const string PlatformIndexPrefix = "chat_platform_index:";

    public async Task<ChatInfo?> GetByUnifiedIdAsync(Guid unifiedId)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = redis.GetDatabase();
            string key = $"{ChatPrefix}{unifiedId}";
            RedisValue json = await db.StringGetAsync(key).WaitAsync(ct);
            return json.HasValue ? JsonSerializer.Deserialize<ChatInfo>(json.ToString()) : null;
        });
    }

    public async Task<ChatInfo?> GetByPlatformIdAsync(string platform, string platformId)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            IDatabase db = redis.GetDatabase();
            string indexKey = $"{PlatformIndexPrefix}{platform.ToLowerInvariant()}:{platformId}";
            RedisValue unifiedId = await db.StringGetAsync(indexKey);
            return unifiedId.HasValue ? await GetByUnifiedIdAsync(Guid.Parse(unifiedId.ToString())) : null;
        });
    }

    public async Task<ChatInfo> CreateOrUpdateAsync(ChatInfo chat)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            IDatabase db = redis.GetDatabase();
            string key = $"{ChatPrefix}{chat.UnifiedId}";
            string json = JsonSerializer.Serialize(chat);

            await db.StringSetAsync(key, json);

            // Updating platform index
            string indexKey = $"{PlatformIndexPrefix}{chat.Platform}:{chat.PlatformId}";
            await db.StringSetAsync(indexKey, chat.UnifiedId.ToString());

            return chat;
        });
    }

    public async Task<bool> ChatExistsAsync(Guid unifiedId)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            IDatabase db = redis.GetDatabase();
            string key = $"{ChatPrefix}{unifiedId}";
            return await db.KeyExistsAsync(key);
        });
    }
}