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
    private readonly IDashboardBridge _bridge;
    private readonly ILogger<MetricsCollector> _logger;

    // Tracking previous values for delta calculation
    private long _prevNetSent, _prevNetRecv;
    private long _prevDiskRead, _prevDiskWrite;
    private DateTime _prevSampleTime = DateTime.UtcNow;
    private TimeSpan _prevCpuTime = TimeSpan.Zero;
    private DateTime _prevCpuSample = DateTime.UtcNow;

    // For CPU calculation on Linux
    private long _prevCpuTotal, _prevCpuIdle;

    public MetricsCollector(
        IConnectionMultiplexer redis,
        IDashboardBridge bridge,
        ILogger<MetricsCollector> logger)
    {
        _redis = redis;
        _bridge = bridge;
        _logger = logger;
    }

    public async Task<MetricsSnapshot> CollectAsync()
    {
        var snapshot = new MetricsSnapshot();
        var now = DateTime.UtcNow;
        var elapsed = (now - _prevSampleTime).TotalSeconds;
        if (elapsed < 0.001) elapsed = 1;
        _prevSampleTime = now;

        // System CPU
        snapshot.CpuPercent = GetSystemCpuPercent();

        // System RAM
        var gcInfo = GC.GetGCMemoryInfo();
        snapshot.RamTotalMb = gcInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0;
        var proc = Process.GetCurrentProcess();
        snapshot.ProcessRamMb = proc.WorkingSet64 / 1024.0 / 1024.0;
        snapshot.RamUsedMb = GetSystemUsedRamMb();
        snapshot.RamPercent = snapshot.RamTotalMb > 0
            ? snapshot.RamUsedMb / snapshot.RamTotalMb * 100
            : 0;

        // Process CPU
        var cpuNow = proc.TotalProcessorTime;
        var cpuElapsed = (now - _prevCpuSample).TotalSeconds;
        snapshot.ProcessCpuPercent = cpuElapsed > 0
            ? (cpuNow - _prevCpuTime).TotalSeconds / cpuElapsed / Environment.ProcessorCount * 100
            : 0;
        _prevCpuTime = cpuNow;
        _prevCpuSample = now;

        // Network
        var (sentBytes, recvBytes) = GetNetworkBytes();
        snapshot.NetSentMbps = (sentBytes - _prevNetSent) / elapsed / 1024.0 / 1024.0;
        snapshot.NetRecvMbps = (recvBytes - _prevNetRecv) / elapsed / 1024.0 / 1024.0;
        _prevNetSent = sentBytes;
        _prevNetRecv = recvBytes;

        // Disk
        var (diskRead, diskWrite) = GetDiskBytes();
        snapshot.DiskReadMbps  = (diskRead  - _prevDiskRead)  / elapsed / 1024.0 / 1024.0;
        snapshot.DiskWriteMbps = (diskWrite - _prevDiskWrite) / elapsed / 1024.0 / 1024.0;
        _prevDiskRead  = diskRead;
        _prevDiskWrite = diskWrite;

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
        snapshot.CommandsPerMinute = _bridge.GetCommandsPerMinute();
        snapshot.MessagesPerMinute = _bridge.GetMessagesPerMinute();

        return snapshot;
    }

    private double GetSystemCpuPercent()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var line = File.ReadLines("/proc/stat").First();
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                long user    = long.Parse(parts[1]);
                long nice    = long.Parse(parts[2]);
                long system  = long.Parse(parts[3]);
                long idle    = long.Parse(parts[4]);
                long iowait  = parts.Length > 5 ? long.Parse(parts[5]) : 0;
                long irq     = parts.Length > 6 ? long.Parse(parts[6]) : 0;
                long softirq = parts.Length > 7 ? long.Parse(parts[7]) : 0;

                long total = user + nice + system + idle + iowait + irq + softirq;
                long active = total - idle - iowait;

                if (_prevCpuTotal == 0)
                {
                    _prevCpuTotal = total;
                    _prevCpuIdle = idle + iowait;
                    return 0;
                }

                var totalDiff = total - _prevCpuTotal;
                var idleDiff = (idle + iowait) - _prevCpuIdle;

                _prevCpuTotal = total;
                _prevCpuIdle = idle + iowait;

                return totalDiff > 0 ? (totalDiff - idleDiff) * 100.0 / totalDiff : 0;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read /proc/stat");
                return 0;
            }
        }

        // Windows
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                return counter.NextValue();
            }
            catch
            {
                return 0;
            }
        }

        return 0;
    }

    private static double GetSystemUsedRamMb()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var lines = File.ReadAllLines("/proc/meminfo");
                long total = 0, available = 0;
                foreach (var line in lines)
                {
                    if (line.StartsWith("MemTotal:"))
                        total = long.Parse(line.Split(':')[1].Trim().Split(' ')[0]);
                    if (line.StartsWith("MemAvailable:"))
                        available = long.Parse(line.Split(':')[1].Trim().Split(' ')[0]);
                }
                return (total - available) / 1024.0; // KB → MB
            }
            catch
            {
                return 0;
            }
        }

        // Windows
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                var totalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0;
                return totalMb * ramCounter.NextValue() / 100.0;
            }
            catch
            {
                return 0;
            }
        }

        return 0;
    }

    private static (long sent, long recv) GetNetworkBytes()
    {
        long sent = 0, recv = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var stats = ni.GetIPv4Statistics();
                sent += stats.BytesSent;
                recv += stats.BytesReceived;
            }
        }
        catch
        {
            //
        }
        return (sent, recv);
    }

    private static (long read, long write) GetDiskBytes()
    {
        // Linux
        if (!OperatingSystem.IsLinux()) return (0, 0);
        long read = 0, write = 0;
        try
        {
            foreach (var line in File.ReadLines("/proc/diskstats"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 10) continue;
                var name = parts[2];
                if (name.StartsWith("loop") || name.StartsWith("ram")) continue;
                if (!char.IsLetter(name[^1])) continue;
                read  += long.Parse(parts[5]) * 512;
                write += long.Parse(parts[9]) * 512;
            }
        }
        catch
        {
            //
        }
        return (read, write);
    }
}
