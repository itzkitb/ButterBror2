using ButterBror.Domain.Entities;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;
using StackExchange.Redis;
using System.Text.Json;

namespace ButterBror.Infrastructure.Data;

public class RedisUserRepository : IUserRepository
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ResiliencePipeline _redisPipeline;
    private readonly ILogger<RedisUserRepository> _logger;
    private const string UserPrefix = "user:";
    private const string PlatformIndexPrefix = "platform_index:";
    private const string DisplayNameIndexPrefix = "display_name_index:";

    public RedisUserRepository(IConnectionMultiplexer redis, ILogger<RedisUserRepository> logger, ResiliencePipelineProvider<string> pipelineProvider)
    {
        _redis = redis;
        _redisPipeline = pipelineProvider.GetPipeline("redis");
        _logger = logger;
    }

    public async Task<UserProfile?> GetByUnifiedIdAsync(Guid unifiedId)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            string key = $"{UserPrefix}{unifiedId}";
            RedisValue json = await db.StringGetAsync(key).WaitAsync(ct);
            return json.HasValue ? JsonSerializer.Deserialize<UserProfile>(json.ToString()) : null;
        });
    }

    public async Task<UserProfile?> GetByPlatformIdAsync(string platform, string platformId)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            string indexKey = $"{PlatformIndexPrefix}{platform.ToLowerInvariant()}:{platformId}";
            RedisValue unifiedId = await db.StringGetAsync(indexKey);
            return unifiedId.HasValue ? await GetByUnifiedIdAsync(Guid.Parse(unifiedId.ToString())) : null;
        });
    }

    public async Task<UserProfile> CreateOrUpdateAsync(UserProfile user)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            string key = $"{UserPrefix}{user.UnifiedUserId}";
            string json = JsonSerializer.Serialize(user);

            await db.StringSetAsync(key, json);

            // Updating platform indexes
            foreach (KeyValuePair<string, string> platform in user.PlatformIds)
            {
                string indexKey = $"{PlatformIndexPrefix}{platform.Key}:{platform.Value}";
                await db.StringSetAsync(indexKey, user.UnifiedUserId.ToString());
            }

            // Updating the index by display name
            string normalized = NormalizeDisplayName(user.DisplayName);
            string displayNameIndexKey = $"{DisplayNameIndexPrefix}{normalized}";
            await db.StringSetAsync(displayNameIndexKey, user.UnifiedUserId.ToString());

            return user;
        });
    }

    public async Task<bool> UserExistsAsync(Guid unifiedId)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            string key = $"{UserPrefix}{unifiedId}";
            return await db.KeyExistsAsync(key);
        });
    }

    public async Task<UserProfile?> GetByDisplayNameAsync(string displayName)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            string normalized = NormalizeDisplayName(displayName);
            string indexKey = $"{DisplayNameIndexPrefix}{normalized}";
            RedisValue unifiedId = await db.StringGetAsync(indexKey);
            return unifiedId.HasValue ? await GetByUnifiedIdAsync(Guid.Parse(unifiedId.ToString())) : null;
        });
    }

    private static string NormalizeDisplayName(string displayName) =>
        displayName.Trim().ToLowerInvariant();
}