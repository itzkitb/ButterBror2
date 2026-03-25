namespace ButterBror.Dashboard.Models;

/// <summary>
/// Result of scanning Redis keys with a cursor
/// </summary>
public record RedisScanResult(long NextCursor, string[] Keys, long TotalScanned);

/// <summary>
/// Detailed information about a Redis key
/// </summary>
public record RedisKeyDetail(
    string Key,
    string Type,  // "string" | "list" | "hash" | "set" | "zset" | "stream" | "unknown"
    long Ttl,     // -1 = no TTL, -2 = key doesn't exist
    long Length,
    object? Value // null if too large
);

/// <summary>
/// Information about a Redis database
/// </summary>
public record RedisDbInfo(int DbIndex, long KeyCount);

/// <summary>
/// Member of a sorted set with its score
/// </summary>
public record ZSetMember(string Member, double Score);

/// <summary>
/// Entry in a Redis stream
/// </summary>
public record RedisStreamEntry(string Id, Dictionary<string, string> Fields);

/// <summary>
/// Request to set a string key
/// </summary>
public record RedisStringSetRequest(string Key, string Value, int TtlSeconds, int Db);

/// <summary>
/// Request to set TTL on a key
/// </summary>
public record RedisTtlSetRequest(string Key, int TtlSeconds, int Db);

/// <summary>
/// Request to persist a key (remove TTL)
/// </summary>
public record RedisPersistRequest(string Key, int Db);

/// <summary>
/// Request to rename a key
/// </summary>
public record RedisRenameRequest(string OldKey, string NewKey, int Db);

/// <summary>
/// Request to set a hash field
/// </summary>
public record RedisHashFieldSetRequest(string Key, string Field, string Value, int Db);

/// <summary>
/// Request to delete a hash field
/// </summary>
public record RedisHashFieldDeleteRequest(string Key, string Field, int Db);

/// <summary>
/// Request to push to a list
/// </summary>
public record RedisListPushRequest(string Key, string Value, bool Tail, int Db);

/// <summary>
/// Request to remove an item from a list
/// </summary>
public record RedisListItemDeleteRequest(string Key, string Value, int Db);

/// <summary>
/// Request to add to a set
/// </summary>
public record RedisSetAddRequest(string Key, string Value, int Db);

/// <summary>
/// Request to remove a member from a set
/// </summary>
public record RedisSetMemberDeleteRequest(string Key, string Value, int Db);

/// <summary>
/// Request to add to a sorted set
/// </summary>
public record RedisZSetAddRequest(string Key, string Member, double Score, int Db);

/// <summary>
/// Request to remove a member from a sorted set
/// </summary>
public record RedisZSetMemberDeleteRequest(string Key, string Member, int Db);
