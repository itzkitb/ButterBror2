using System.Text;
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
    private const string PlatformIndexPrefix = "index:user_id:";
    private const string DisplayNameIndexPrefix = "index:user_name:";

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
            var normalized = NormalizeDisplayName(user.DisplayName);
            foreach (var platform in user.PlatformIds)
            {
                var indexKey = $"{PlatformIndexPrefix}{platform.Key}:{platform.Value}";
                var displayNameIndexKey = $"{DisplayNameIndexPrefix}{platform.Key}:{normalized}";
                await db.StringSetAsync(indexKey, user.UnifiedId.ToString());
                await db.StringSetAsync(displayNameIndexKey, user.UnifiedId.ToString());
            }
            
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

    public async Task<UserProfile?> GetByDisplayNameAsync(string displayName, string platform)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var normalized = NormalizeDisplayName(displayName);
            var indexKey = $"{DisplayNameIndexPrefix}{platform}:{normalized}";
            var unifiedId = await db.StringGetAsync(indexKey);
            return unifiedId.HasValue ? await GetByUnifiedIdAsync(Guid.Parse(unifiedId.ToString())) : null;
        });
    }

    public async Task<UserProfile?> FindUserAsync(string platform, string identifier)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var user = await GetByPlatformIdAsync(platform, identifier) ?? await GetByDisplayNameAsync(identifier, platform);
            return user;
        });
    }

    private static string NormalizeDisplayName(string displayName) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(displayName.Trim().ToLowerInvariant()));
}