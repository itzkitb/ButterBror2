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
    private const string PlatformIndexPrefix = "chat_platform_index:";

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

    public async Task<ChatInfo> CreateOrUpdateAsync(ChatInfo chat)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var key = $"{ChatPrefix}{chat.UnifiedId}";
            var json = JsonSerializer.Serialize(chat);

            await db.StringSetAsync(key, json);

            // updating platform index
            var indexKey = $"{PlatformIndexPrefix}{chat.Platform}:{chat.PlatformId}";
            await db.StringSetAsync(indexKey, chat.UnifiedId.ToString());

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