using System.Text.Json;
using ButterBror.Data.Interfaces;
using ButterBror.Domain.Entities;
using Polly;
using Polly.Registry;
using StackExchange.Redis;

namespace ButterBror.Data.Repositories;

public class RedisUserRepository(
    IConnectionMultiplexer redis,
    ResiliencePipelineProvider<string> pipelineProvider)
    : IUserRepository
{
    private readonly ResiliencePipeline _redisPipeline = pipelineProvider.GetPipeline("redis");
    private const string UserPrefix = "user:";
    private const string PlatformIndexPrefix = "platform_index:";
    private const string DisplayNameIndexPrefix = "display_name_index:";

    public async Task<UserProfile?> GetByUnifiedIdAsync(Guid unifiedId)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            var db = redis.GetDatabase();
            var key = $"{UserPrefix}{unifiedId}";
            var json = await db.StringGetAsync(key).WaitAsync(ct);
            return json.HasValue ? JsonSerializer.Deserialize<UserProfile>(json.ToString()) : null;
        });
    }

    public async Task<UserProfile?> GetByPlatformIdAsync(string platform, string platformId)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var indexKey = $"{PlatformIndexPrefix}{platform.ToLowerInvariant()}:{platformId}";
            var unifiedId = await db.StringGetAsync(indexKey);
            return unifiedId.HasValue ? await GetByUnifiedIdAsync(Guid.Parse(unifiedId.ToString())) : null;
        });
    }

    public async Task<UserProfile> CreateOrUpdateAsync(UserProfile user)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var key = $"{UserPrefix}{user.UnifiedId}";
            var json = JsonSerializer.Serialize(user);

            await db.StringSetAsync(key, json);

            // updating platform indexes
            foreach (var indexKey in user.PlatformIds.Select(platform => 
                         $"{PlatformIndexPrefix}{platform.Key}:{platform.Value}"))
            {
                await db.StringSetAsync(indexKey, user.UnifiedId.ToString());
            }

            // updating the index by display name
            var normalized = NormalizeDisplayName(user.DisplayName);
            var displayNameIndexKey = $"{DisplayNameIndexPrefix}{normalized}";
            await db.StringSetAsync(displayNameIndexKey, user.UnifiedId.ToString());

            return user;
        });
    }

    public async Task<bool> UserExistsAsync(Guid unifiedId)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var key = $"{UserPrefix}{unifiedId}";
            return await db.KeyExistsAsync(key);
        });
    }

    public async Task<UserProfile?> GetByDisplayNameAsync(string displayName)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var normalized = NormalizeDisplayName(displayName);
            var indexKey = $"{DisplayNameIndexPrefix}{normalized}";
            var unifiedId = await db.StringGetAsync(indexKey);
            return unifiedId.HasValue ? await GetByUnifiedIdAsync(Guid.Parse(unifiedId.ToString())) : null;
        });
    }

    public async Task<UserProfile?> FindUserAsync(string platform, string identifier)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var user = await GetByPlatformIdAsync(platform, identifier) ?? await GetByDisplayNameAsync(identifier);
            return user;
        });
    }

    private static string NormalizeDisplayName(string displayName) =>
        displayName.Trim().ToLowerInvariant();
}