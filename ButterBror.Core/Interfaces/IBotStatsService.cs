namespace ButterBror.Core.Interfaces;

/// <summary>
/// Bot statistics service providing live metrics and persistent counters
/// </summary>
public interface IBotStatsService : IControlledService
{
    // ><> properties
    // ^ live

    /// <summary>
    /// Current commands per minute rate
    /// </summary>
    double CommandsPerMinute { get; }

    /// <summary>
    /// Current messages per minute rate
    /// </summary>
    double MessagesPerMinute { get; }

    // ^ redis

    /// <summary>
    /// Last known Redis memory usage in bytes
    /// </summary>
    long RedisMemoryUsedBytes { get; }

    /// <summary>
    /// Last known number of connected Redis clients
    /// </summary>
    long RedisConnectedClients { get; }

    /// <summary>
    /// Last known Redis operations per second
    /// </summary>
    long RedisOpsPerSecond { get; }

    /// <summary>
    /// Redis operations per minute
    /// </summary>
    long RedisOpsPerMinute { get; }

    /// <summary>
    /// Redis operations per hour
    /// </summary>
    long RedisOpsPerHour { get; }

    /// <summary>
    /// Redis keys
    /// </summary>
    long RedisTotalKeys { get; }
    
    // ^ uptime

    /// <summary>
    /// Current session uptime
    /// </summary>
    TimeSpan CurrentSessionUptime { get; }

    // ^ persistent

    /// <summary>
    /// Total commands executed across all sessions
    /// </summary>
    long TotalCommandsExecuted { get; }

    /// <summary>
    /// Total replies sent across all sessions
    /// </summary>
    long TotalRepliesSent { get; }

    /// <summary>
    /// Total uptime across all sessions
    /// </summary>
    TimeSpan TotalUptime { get; }

    // ><> methods

    /// <summary>
    /// Increment the command counter
    /// </summary>
    void IncrementCommandCount();

    /// <summary>
    /// Increment the message counter
    /// </summary>
    void IncrementMessageCount();

    /// <summary>
    /// Increment the replies counter
    /// </summary>
    void IncrementRepliesCount();
    
    // ^ redis

    /// <summary>
    /// Update Redis statistics
    /// </summary>
    /// <param name="memoryUsedBytes">Redis memory used in bytes</param>
    /// <param name="connectedClients">Number of connected clients</param>
    /// <param name="opsPerSecond">Operations per second</param>
    /// <param name="keys">Total number of keys in the database</param>
    void UpdateRedisStats(long memoryUsedBytes, long connectedClients, long opsPerSecond, long keys);

    // ^ init

    /// <summary>
    /// Flush persistent stats to disk
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
