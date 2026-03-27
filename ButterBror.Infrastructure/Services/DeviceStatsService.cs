using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;

namespace ButterBror.Infrastructure.Services;

public class DeviceStatsService : IDeviceStatsService, IDisposable
{
    private ILogger<DeviceStatsService> _logger;

    // Public
    public double CpuLoad => _cpuLoad;
    public double CpuTemperature => _cpuTemp;
    public double TotalMemory => _totalMem;
    public double MemoryUsed => _memUsed;
    public double NetworkIn => _netIn;
    public double NetworkOut => _netOut;
    public double DiskIn => _diskIn;
    public double DiskOut => _diskOut;

    // Private
    private double _cpuLoad, _cpuTemp, _totalMem, _memUsed, _netIn, _netOut, _diskIn, _diskOut;

    // Tracking previous values for delta calculation
    private long _prevNetSent, _prevNetRecv;
    private long _prevDiskRead, _prevDiskWrite;
    private DateTime _prevSampleTime = DateTime.UtcNow;
    private long _prevCpuTotal, _prevCpuIdle;

    // Task
    private Task _updateTask = Task.CompletedTask;
    private CancellationTokenSource _cts = null!;
    

    public DeviceStatsService(
        ILogger<DeviceStatsService> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _updateTask = Task.Run(() => MetricsLoopAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("DeviceStatsService started");
    }

    public void Stop()
    {
        _cts.Cancel();
        _updateTask.GetAwaiter().GetResult();
        _logger.LogInformation("DeviceStatsService stopped");
    }

    private async Task MetricsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2_000, ct);
                var now = DateTime.UtcNow;
                var elapsed = (now - _prevSampleTime).TotalSeconds;

                // System CPU
                _cpuLoad = GetSystemCpuPercent();
                _cpuTemp = GetCpuTemperature();

                // System RAM
                var gcInfo = GC.GetGCMemoryInfo();
                _totalMem = gcInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0;
                _memUsed = GetSystemUsedRamMb();
                
                var (sentBytes, recvBytes) = GetNetworkBytes();
                _netOut = (sentBytes - _prevNetSent) / elapsed / 1024.0 / 1024.0;
                _netIn  = (recvBytes - _prevNetRecv) / elapsed / 1024.0 / 1024.0;
                _prevNetSent = sentBytes;
                _prevNetRecv = recvBytes;

                // Disk
                var (diskRead, diskWrite) = GetDiskBytes();
                _diskIn  = (diskRead  - _prevDiskRead)  / elapsed / 1024.0 / 1024.0;
                _diskOut = (diskWrite - _prevDiskWrite) / elapsed / 1024.0 / 1024.0;
                _prevDiskRead  = diskRead;
                _prevDiskWrite = diskWrite;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Metrics receive error");
            }
        }
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
                return (total - available) / 1024.0; // KB -> MB
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

    public static double GetCpuTemperature()
    {
        // Linux
        if (OperatingSystem.IsLinux())
        {
            try
            {
                string thermalPath = "/sys/class/thermal/thermal_zone0/temp";

                if (File.Exists(thermalPath))
                {
                    string tempRaw = File.ReadAllText(thermalPath).Trim();
                    if (double.TryParse(tempRaw, out double tempMilli))
                    {
                        // Millidegrees Celsius -> Degrees Celsius
                        return tempMilli / 1000.0;
                    }
                }
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
                // Some hardware/drivers do not report to MSAcpi_ThermalZoneTemperature
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                var query = searcher.Get().Cast<ManagementObject>();

                foreach (var obj in query)
                {
                    // Tenths of degrees Kelvin -> Degrees Celsius
                    double temp = Convert.ToDouble(obj["CurrentTemperature"]);
                    return (temp - 2732) / 10.0; 
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Windows WMI Error: {ex.Message}");
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
        if (OperatingSystem.IsLinux())
        {
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

        // Windows
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var readCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
                using var writeCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

                return (readCounter.RawValue, writeCounter.RawValue);
            }
            catch
            {
                return (0, 0);
            }
        }

        return (0, 0);
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }
}