using System.Diagnostics;
using System.Net.NetworkInformation;
using ButterBror.Core.Interfaces;
using ButterBror.Dashboard.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ButterBror.Dashboard.Services;

/// <summary>
/// Collects system and bot metrics for the dashboard
/// </summary>
public class MetricsCollector
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IBotStatsService _stats;
    private readonly ILogger<MetricsCollector> _logger;
    private readonly IDeviceStatsService _deviceStats;

    // Tracking previous values for delta calculation
    private DateTime _prevSampleTime = DateTime.UtcNow;
    private TimeSpan _prevCpuTime = TimeSpan.Zero;
    private DateTime _prevCpuSample = DateTime.UtcNow;

    public MetricsCollector(
        IConnectionMultiplexer redis,
        IBotStatsService stats,
        IDeviceStatsService deviceStats,
        ILogger<MetricsCollector> logger)
    {
        _redis = redis;
        _stats = stats;
        _logger = logger;
        _deviceStats = deviceStats;
    }

    public async Task<MetricsSnapshot> CollectAsync()
    {
        var snapshot = new MetricsSnapshot();
        var now = DateTime.UtcNow;
        var elapsed = (now - _prevSampleTime).TotalSeconds;
        if (elapsed < 0.001) elapsed = 1;
        _prevSampleTime = now;

        // System
        snapshot.CpuPercent  = _deviceStats.CpuLoad;
        snapshot.RamTotalMb  = _deviceStats.TotalMemory;
        snapshot.RamUsedMb   = _deviceStats.MemoryUsed;
        snapshot.RamPercent  = _deviceStats.TotalMemory > 0
            ? _deviceStats.MemoryUsed / _deviceStats.TotalMemory * 100 : 0;
        snapshot.NetSentMbps = _deviceStats.NetworkOut;
        snapshot.NetRecvMbps = _deviceStats.NetworkIn;
        snapshot.DiskReadMbps  = _deviceStats.DiskIn;
        snapshot.DiskWriteMbps = _deviceStats.DiskOut;

        // Process
        var proc = Process.GetCurrentProcess();
        snapshot.ProcessRamMb = proc.WorkingSet64 / 1024.0 / 1024.0;
        var cpuNow = proc.TotalProcessorTime;
        var cpuElapsed = (now - _prevCpuSample).TotalSeconds;
        snapshot.ProcessCpuPercent = cpuElapsed > 0
            ? (cpuNow - _prevCpuTime).TotalSeconds / cpuElapsed / Environment.ProcessorCount * 100
            : 0;
        _prevCpuTime = cpuNow;
        _prevCpuSample = now;

        // Redis
        try
        {
            var endPoint = _redis.GetEndPoints().FirstOrDefault();
            if (endPoint != null)
            {
                var server = _redis.GetServer(endPoint);
                var info = await server.InfoAsync();

                var flatInfo = info.SelectMany(g => g).ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value,
                    StringComparer.OrdinalIgnoreCase
                );

                if (flatInfo.TryGetValue("used_memory", out var usedMemStr) &&
                    long.TryParse(usedMemStr, out var usedMem))
                {
                    snapshot.RedisMemoryUsedBytes = usedMem;
                }

                if (flatInfo.TryGetValue("connected_clients", out var clientsStr) &&
                    long.TryParse(clientsStr, out var clients))
                {
                    snapshot.RedisConnectedClients = clients;
                }

                if (flatInfo.TryGetValue("instantaneous_ops_per_sec", out var opsStr) &&
                    long.TryParse(opsStr, out var ops))
                {
                    snapshot.RedisOpsPerSecond = ops;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to collect Redis metrics (allowAdmin may be required)");
        }

        // Bot
        snapshot.CommandsPerMinute = _stats.CommandsPerMinute;
        snapshot.MessagesPerMinute = _stats.MessagesPerMinute;
        snapshot.RedisOpsPerMinute = _stats.RedisOpsPerMinute;
        snapshot.RedisOpsPerHour = _stats.RedisOpsPerHour;
        snapshot.BotSessionUptime = _stats.CurrentSessionUptime;
        snapshot.TotalCommandsExecuted = _stats.TotalCommandsExecuted;
        snapshot.TotalRepliesSent = _stats.TotalRepliesSent;
        snapshot.TotalUptime = _stats.TotalUptime;

        // Update Redis stats in the service
        _stats.UpdateRedisStats(
            snapshot.RedisMemoryUsedBytes,
            snapshot.RedisConnectedClients,
            snapshot.RedisOpsPerSecond);

        return snapshot;
    }
}
