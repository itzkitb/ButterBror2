namespace ButterBror.Dashboard.Models;

/// <summary>
/// Snapshot of system and bot metrics at a point in time
/// </summary>
public class MetricsSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // System
    public double CpuPercent { get; set; }
    public double RamTotalMb { get; set; }
    public double RamUsedMb { get; set; }
    public double RamPercent { get; set; }

    // Process
    public double ProcessCpuPercent { get; set; }
    public double ProcessRamMb { get; set; }

    // Network (delta since last snapshot)
    public double NetSentMbps { get; set; }
    public double NetRecvMbps { get; set; }

    // Disk (delta since last snapshot)
    public double DiskReadMbps { get; set; }
    public double DiskWriteMbps { get; set; }

    // Redis
    public long RedisMemoryUsedBytes { get; set; }
    public long RedisConnectedClients { get; set; }
    public long RedisOpsPerSecond { get; set; }
    public long RedisOpsPerMinute { get; set; }
    public long RedisOpsPerHour { get; set; }

    // Bot
    public double CommandsPerMinute { get; set; }
    public double MessagesPerMinute { get; set; }
    public TimeSpan BotSessionUptime { get; set; }
    public long TotalCommandsExecuted { get; set; }
    public long TotalRepliesSent { get; set; }
    public TimeSpan TotalUptime { get; set; }
}
