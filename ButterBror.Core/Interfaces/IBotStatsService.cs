#nullable enable

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Bot statistics service providing live metrics and persistent counters
/// </summary>
public interface IBotStatsService
{
    // Live

    /// <summary>
    /// Current commands per minute rate
    /// </summary>
    double CommandsPerMinute { get; }

    /// <summary>
    /// Current messages per minute rate
    /// </summary>
    double MessagesPerMinute { get; }

    // Redis

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
    /// Redis operations per minute (rolling window)
    /// </summary>
    long RedisOpsPerMinute { get; }

    /// <summary>
    /// Redis operations per hour (rolling window)
    /// </summary>
    long RedisOpsPerHour { get; }

    // Uptime

    /// <summary>
    /// Current session uptime
    /// </summary>
    TimeSpan CurrentSessionUptime { get; }

    // Persistent

    /// <summary>
    /// Total commands executed across all sessions
    /// </summary>
    long TotalCommandsExecuted { get; }

    /// <summary>
    /// Total replies sent across all sessions
    /// </summary>
    long TotalRepliesSent { get; }

    /// <summary>
    /// Total uptime across all sessions (including current)
    /// </summary>
    TimeSpan TotalUptime { get; }

    // Methods

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

    // Redis

    /// <summary>
    /// Update Redis statistics
    /// </summary>
    /// <param name="memoryUsedBytes">Redis memory used in bytes</param>
    /// <param name="connectedClients">Number of connected clients</param>
    /// <param name="opsPerSecond">Operations per second</param>
    void UpdateRedisStats(long memoryUsedBytes, long connectedClients, long opsPerSecond);

    // Initialize

    /// <summary>
    /// Initialize the service (load persistent stats)
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Flush persistent stats to disk
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
