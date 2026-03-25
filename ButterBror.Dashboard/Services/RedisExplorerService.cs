using ButterBror.Dashboard.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ButterBror.Dashboard.Services;

/// <summary>
/// Service for Redis exploration operations
/// </summary>
public class RedisExplorerService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisExplorerService> _logger;

    // Limits for large data
    private const int MaxStringValueSize = 512 * 1024; // 512 KB
    private const int MaxCollectionElements = 10_000;
    private const int DefaultCollectionPreview = 500;

    public RedisExplorerService(
        IConnectionMultiplexer redis,
        ILogger<RedisExplorerService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private IDatabase GetDb(int dbIndex = 0)
    {
        return _redis.GetDatabase(dbIndex);
    }

    /// <summary>
    /// Scan keys with pattern and cursor
    /// </summary>
    public async Task<RedisScanResult> ScanKeysAsync(string pattern, long cursor, int count, int db = 0)
    {
        var database = GetDb(db);
        var keys = new List<string>();
        long totalScanned = 0;

        var server = GetServer();
        var allKeys = server.Keys(database: db, pattern: pattern);
        
        foreach (var key in allKeys)
        {
            keys.Add(key.ToString()!);
            totalScanned++;
            if (keys.Count >= count)
                break;
        }

        var nextCursor = keys.Count >= count ? cursor + count : 0;

        return new RedisScanResult(nextCursor, keys.ToArray(), totalScanned);
    }

    /// <summary>
    /// Get full details about a key
    /// </summary>
    public async Task<RedisKeyDetail?> GetKeyDetailAsync(string key, int db = 0)
    {
        var database = GetDb(db);
        var redisKey = new RedisKey(key);

        if (!await database.KeyExistsAsync(redisKey))
            return null;

        // Type
        var type = await database.KeyTypeAsync(redisKey);
        var typeString = type switch
        {
            RedisType.String => "string",
            RedisType.List => "list",
            RedisType.Hash => "hash",
            RedisType.Set => "set",
            RedisType.SortedSet => "zset",
            RedisType.Stream => "stream",
            _ => "unknown"
        };

        // TTL
        var ttl = await database.KeyTimeToLiveAsync(redisKey);
        var ttlSeconds = ttl.HasValue ? (long)ttl.Value.TotalSeconds : -1;

        // Value
        long length = 0;
        object? value = null;

        switch (type)
        {
            case RedisType.String:
                length = await database.StringLengthAsync(redisKey);
                if (length <= MaxStringValueSize)
                {
                    var strValue = await database.StringGetAsync(redisKey);
                    value = strValue.ToString();
                }
                break;

            case RedisType.List:
                length = await database.ListLengthAsync(redisKey);
                if (length <= MaxCollectionElements)
                {
                    var elements = await database.ListRangeAsync(redisKey, 0, DefaultCollectionPreview - 1);
                    value = elements.Select(e => e.ToString()).ToArray();
                }
                else
                {
                    var elements = await database.ListRangeAsync(redisKey, 0, DefaultCollectionPreview - 1);
                    value = elements.Select(e => e.ToString()).ToArray();
                }
                break;

            case RedisType.Hash:
                length = await database.HashLengthAsync(redisKey);
                var hashEntries = await database.HashGetAllAsync(redisKey);
                value = hashEntries.Take(DefaultCollectionPreview).ToDictionary(
                    e => e.Name.ToString()!,
                    e => e.Value.ToString()!);
                break;

            case RedisType.Set:
                length = await database.SetLengthAsync(redisKey);
                var setElements = await database.SetMembersAsync(redisKey);
                value = setElements.Take(DefaultCollectionPreview).Select(e => e.ToString()).ToArray();
                break;

            case RedisType.SortedSet:
                length = await database.SortedSetLengthAsync(redisKey);
                var zsetElements = await database.SortedSetRangeByRankWithScoresAsync(redisKey, 0, DefaultCollectionPreview - 1);
                value = zsetElements.Select(e => new ZSetMember(e.Element.ToString()!, e.Score)).ToArray();
                break;

            case RedisType.Stream:
                length = await database.StreamLengthAsync(redisKey);
                var streamEntries = await database.StreamRangeAsync(redisKey, "-", "+", count: DefaultCollectionPreview);
                value = streamEntries.Select(e => new RedisStreamEntry(
                    e.Id.ToString()!,
                    e.Values.ToDictionary(v => v.Name.ToString()!, v => v.Value.ToString()!)
                )).ToArray();
                break;
        }

        return new RedisKeyDetail(key, typeString, ttlSeconds, length, value);
    }

    /// <summary>
    /// Set or update a STRING key
    /// </summary>
    public async Task SetStringAsync(string key, string value, TimeSpan? ttl, int db = 0)
    {
        var database = GetDb(db);
        var redisKey = new RedisKey(key);
        var redisValue = new RedisValue(value);

        if (ttl.HasValue)
        {
            await database.StringSetAsync(redisKey, redisValue, TimeSpan.FromSeconds(ttl.Value.TotalSeconds), flags: CommandFlags.PreferMaster);
        }
        else
        {
            await database.StringSetAsync(redisKey, redisValue, flags: CommandFlags.PreferMaster);
        }
    }

    /// <summary>
    /// Delete a key
    /// </summary>
    public async Task<bool> DeleteKeyAsync(string key, int db = 0)
    {
        var database = GetDb(db);
        return await database.KeyDeleteAsync(new RedisKey(key));
    }

    /// <summary>
    /// Set TTL on an existing key
    /// </summary>
    public async Task<bool> SetTtlAsync(string key, TimeSpan ttl, int db = 0)
    {
        var database = GetDb(db);
        return await database.KeyExpireAsync(new RedisKey(key), ttl);
    }

    /// <summary>
    /// Remove TTL from a key
    /// </summary>
    public async Task<bool> PersistKeyAsync(string key, int db = 0)
    {
        var database = GetDb(db);
        return await database.KeyPersistAsync(new RedisKey(key));
    }

    /// <summary>
    /// Rename a key
    /// </summary>
    public async Task<bool> RenameKeyAsync(string oldKey, string newKey, int db = 0)
    {
        var database = GetDb(db);
        try
        {
            await database.KeyRenameAsync(new RedisKey(oldKey), new RedisKey(newKey));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rename key {OldKey} to {NewKey}", oldKey, newKey);
            return false;
        }
    }

    /// <summary>
    /// Get list of available databases
    /// </summary>
    public async Task<RedisDbInfo[]> GetDatabasesInfoAsync()
    {
        var result = new List<RedisDbInfo>();
        var server = GetServer();

        // CONFIG GET
        int dbCount = 16;
        try
        {
            var config = await server.ConfigGetAsync("databases");
            if (config.Length > 0 && int.TryParse(config[0].Value, out var count))
            {
                dbCount = count;
            }
        }
        catch
        {
            // 
        }

        // Get key count for each db
        for (int i = 0; i < dbCount; i++)
        {
            var database = GetDb(i);
            try
            {
                var size = await database.ExecuteAsync("DBSIZE");
                var keyCount = size.IsNull ? 0 : (long)size;
                result.Add(new RedisDbInfo(i, keyCount));
            }
            catch
            {
                result.Add(new RedisDbInfo(i, 0));
            }
        }

        return result.ToArray();
    }

    private IServer GetServer()
    {
        var endPoint = _redis.GetEndPoints().FirstOrDefault();
        if (endPoint == null)
            throw new InvalidOperationException("No Redis endpoints available");
        return _redis.GetServer(endPoint);
    }

    /// <summary>
    /// Hash: HGETALL
    /// </summary>
    public async Task<Dictionary<string, string>> HashGetAllAsync(string key, int db = 0)
    {
        var database = GetDb(db);
        var entries = await database.HashGetAllAsync(new RedisKey(key));
        return entries.ToDictionary(
            e => e.Name.ToString()!,
            e => e.Value.ToString()!);
    }

    /// <summary>
    /// Hash: HSET field value
    /// </summary>
    public async Task HSetAsync(string key, string field, string value, int db = 0)
    {
        var database = GetDb(db);
        await database.HashSetAsync(
            new RedisKey(key),
            new RedisValue(field),
            new RedisValue(value),
            flags: CommandFlags.PreferMaster);
    }

    /// <summary>
    /// Hash: HDEL field
    /// </summary>
    public async Task<bool> HDelAsync(string key, string field, int db = 0)
    {
        var database = GetDb(db);
        var deleted = await database.HashDeleteAsync(
            new RedisKey(key),
            new RedisValue(field));
        return deleted;
    }

    /// <summary>
    /// List: LRANGE 0 -1
    /// </summary>
    public async Task<string[]> ListGetAllAsync(string key, int db = 0)
    {
        var database = GetDb(db);
        var elements = await database.ListRangeAsync(new RedisKey(key), -1, DefaultCollectionPreview);
        return elements.Select(e => e.ToString()!).ToArray();
    }

    /// <summary>
    /// List: LPUSH/RPUSH
    /// </summary>
    public async Task ListPushAsync(string key, string value, bool tail, int db = 0)
    {
        var database = GetDb(db);
        if (tail)
        {
            await database.ListRightPushAsync(new RedisKey(key), new RedisValue(value), flags: CommandFlags.PreferMaster);
        }
        else
        {
            await database.ListLeftPushAsync(new RedisKey(key), new RedisValue(value), flags: CommandFlags.PreferMaster);
        }
    }

    /// <summary>
    /// List: LREM (remove all occurrences)
    /// </summary>
    public async Task<long> ListRemoveAsync(string key, string value, int db = 0)
    {
        var database = GetDb(db);
        return await database.ListRemoveAsync(new RedisKey(key), new RedisValue(value), 0);
    }

    /// <summary>
    /// Set: SMEMBERS
    /// </summary>
    public async Task<string[]> SetGetAllAsync(string key, int db = 0)
    {
        var database = GetDb(db);
        var elements = await database.SetMembersAsync(new RedisKey(key));
        return elements.Select(e => e.ToString()!).Take(DefaultCollectionPreview).ToArray();
    }

    /// <summary>
    /// Set: SADD
    /// </summary>
    public async Task SetAddAsync(string key, string value, int db = 0)
    {
        var database = GetDb(db);
        await database.SetAddAsync(new RedisKey(key), new RedisValue(value), flags: CommandFlags.PreferMaster);
    }

    /// <summary>
    /// Set: SREM
    /// </summary>
    public async Task<bool> SetRemoveAsync(string key, string value, int db = 0)
    {
        var database = GetDb(db);
        return await database.SetRemoveAsync(new RedisKey(key), new RedisValue(value));
    }

    /// <summary>
    /// ZSet: ZRANGE 0 -1 WITHSCORES
    /// </summary>
    public async Task<ZSetMember[]> ZSetGetAllAsync(string key, int db = 0)
    {
        var database = GetDb(db);
        var elements = await database.SortedSetRangeByRankWithScoresAsync(new RedisKey(key), 0, DefaultCollectionPreview - 1);
        return elements.Select(e => new ZSetMember(e.Element.ToString()!, e.Score)).ToArray();
    }

    /// <summary>
    /// ZSet: ZADD
    /// </summary>
    public async Task ZSetAddAsync(string key, string member, double score, int db = 0)
    {
        var database = GetDb(db);
        await database.SortedSetAddAsync(
            new RedisKey(key),
            new RedisValue(member),
            score,
            flags: CommandFlags.PreferMaster);
    }

    /// <summary>
    /// ZSet: ZREM
    /// </summary>
    public async Task<bool> ZSetRemoveAsync(string key, string member, int db = 0)
    {
        var database = GetDb(db);
        return await database.SortedSetRemoveAsync(
            new RedisKey(key),
            new RedisValue(member));
    }

    /// <summary>
    /// Stream: XRANGE - + COUNT (read-only)
    /// </summary>
    public async Task<RedisStreamEntry[]> StreamReadAsync(string key, int count, int db = 0)
    {
        var database = GetDb(db);
        var entries = await database.StreamRangeAsync(
            new RedisKey(key),
            "-",
            "+",
            count: count);

        return entries.Select(e => new RedisStreamEntry(
            e.Id.ToString()!,
            e.Values.ToDictionary(v => v.Name.ToString()!, v => v.Value.ToString()!)
        )).ToArray();
    }
}
