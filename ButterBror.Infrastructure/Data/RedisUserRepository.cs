using ButterBror.Domain.Entities;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace ButterBror.Infrastructure.Data;

public class RedisUserRepository : IUserRepository
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisUserRepository> _logger;
    private const string UserPrefix = "user:";
    private const string PlatformIndexPrefix = "platform_index:";

    public RedisUserRepository(IConnectionMultiplexer redis, ILogger<RedisUserRepository> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<UserProfile?> GetByUnifiedIdAsync(Guid unifiedId)
    {
        IDatabase db = _redis.GetDatabase();
        string key = $"{UserPrefix}{unifiedId}";

        RedisValue json = await db.StringGetAsync(key);
        if (!json.HasValue) return null;

        return JsonSerializer.Deserialize<UserProfile>(json.ToString());
    }

    public async Task<UserProfile?> GetByPlatformIdAsync(string platform, string platformId)
    {
        IDatabase db = _redis.GetDatabase();
        string indexKey = $"{PlatformIndexPrefix}{platform.ToLowerInvariant()}:{platformId}";

        RedisValue unifiedId = await db.StringGetAsync(indexKey);
        if (!unifiedId.HasValue) return null;

        return await GetByUnifiedIdAsync(Guid.Parse(unifiedId.ToString()));
    }

    public async Task<UserProfile> CreateOrUpdateAsync(UserProfile user)
    {
        IDatabase db = _redis.GetDatabase();
        string key = $"{UserPrefix}{user.UnifiedUserId}";
        string json = JsonSerializer.Serialize(user);

        await db.StringSetAsync(key, json);

        // Обновляем индексы для платформ
        foreach (KeyValuePair<string, string> platform in user.PlatformIds)
        {
            string indexKey = $"{PlatformIndexPrefix}{platform.Key}:{platform.Value}";
            await db.StringSetAsync(indexKey, user.UnifiedUserId.ToString());
        }

        return user;
    }

    public async Task<bool> UserExistsAsync(Guid unifiedId)
    {
        IDatabase db = _redis.GetDatabase();
        string key = $"{UserPrefix}{unifiedId}";
        return await db.KeyExistsAsync(key);
    }

    public Task<List<UserProfile>> GetAllUsersAsync()
    {
        throw new NotImplementedException();
    }
}