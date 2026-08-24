using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace ButterBror.Infrastructure.Services;

// 
// i fucking hate this piece of code.
// 

public class DeviceStatsService(ILogger<DeviceStatsService> logger) : IDeviceStatsService, IDisposable
{
    private readonly ILogger<DeviceStatsService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private CpuTemperatureReader? _cpuTempReader;
    private CancellationTokenSource? _cts;
    private Task? _updateTask;

    // ><> public properties for metrics
    public double CpuLoad { get; private set; }
    public double CpuTemperature { get; private set; }
    public double TotalMemory { get; private set; }
    public double MemoryUsed { get; private set; }
    public double NetworkIn { get; private set; }
    public double NetworkOut { get; private set; }
    public double DiskIn { get; private set; }
    public double DiskOut { get; private set; }

    // ><> tracking previous values for delta calculation
    private long _prevNetSent, _prevNetRecv;
    private long _prevDiskRead, _prevDiskWrite;
    private long _prevCpuTotal, _prevCpuIdle;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _cts = new CancellationTokenSource();
        _cpuTempReader = new CpuTemperatureReader(_logger);
        _updateTask = Task.Run(() => MetricsLoopAsync(_cts.Token), cancellationToken);
        
        _logger.LogInformation("[init:ok] device status service");
        return Task.CompletedTask;
    }
    
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (_cts == null || _updateTask == null)
        {
            _logger.LogWarning("[stop:skip] service not initialized");
            return;
        }

        try
        {
            await _cts.CancelAsync();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(5));
            
            await _updateTask.WaitAsync(linkedCts.Token);
            
            _logger.LogInformation("[stop:ok] device status service");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[stop:timeout] device status service shutdown timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "error during device status service shutdown");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }
    
    private async Task MetricsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2_000, ct);

                // s0: cpu
                CpuLoad = GetSystemCpuPercent();
                if (_cpuTempReader != null)
                {
                    CpuTemperature = _cpuTempReader.Read() ?? 0;
                }

                // s1: ram
                var gcInfo = GC.GetGCMemoryInfo();
                TotalMemory = gcInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0;
                MemoryUsed = GetSystemUsedRamMb();
                
                var (sentBytes, receiveBytes) = GetNetworkBytes();
                NetworkOut = (sentBytes - _prevNetSent) / 2.0 / 1024.0;
                NetworkIn  = (receiveBytes - _prevNetRecv) / 2.0 / 1024.0;
                _prevNetSent = sentBytes;
                _prevNetRecv = receiveBytes;

                // s2: disk
                var (diskRead, diskWrite) = GetDiskBytes();
                DiskIn  = (diskRead  - _prevDiskRead)  / 2.0 / 1024.0;
                DiskOut = (diskWrite - _prevDiskWrite) / 2.0 / 1024.0;
                _prevDiskRead  = diskRead;
                _prevDiskWrite = diskWrite;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("metrics loop cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "metrics receive error");
            }
        }
    }

    private double GetSystemCpuPercent()
    {
        return OperatingSystem.IsWindows() ? GetSystemCpuPercentWindows() : GetSystemCpuPercentLinux();
    }

    private double GetSystemCpuPercentWindows()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return 0;
            
            using var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            counter.NextValue();
            Thread.Sleep(100);
            return counter.NextValue();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "failed to read CPU stats on Windows");
            return 0;
        }
    }

    private double GetSystemCpuPercentLinux()
    {
        try
        {
            var line = File.ReadLines("/proc/stat").First();
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var user = long.Parse(parts[1]);
            var nice = long.Parse(parts[2]);
            var system = long.Parse(parts[3]);
            var idle = long.Parse(parts[4]);
            var iowait = parts.Length > 5 ? long.Parse(parts[5]) : 0;
            var irq = parts.Length > 6 ? long.Parse(parts[6]) : 0;
            var softirq = parts.Length > 7 ? long.Parse(parts[7]) : 0;

            var total = user + nice + system + idle + iowait + irq + softirq;

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
            _logger.LogDebug(ex, "failed to read CPU stats on Linux");
            return 0;
        }
    }

    private static double GetSystemUsedRamMb()
    {
        return OperatingSystem.IsWindows() ? GetSystemUsedRamMbWindows() : GetSystemUsedRamMbLinux();
    }

    private static double GetSystemUsedRamMbWindows()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return 0;
            
            using var ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
            var totalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0;
            return totalMb * ramCounter.NextValue() / 100.0;

        }
        catch
        {
            return 0;
        }
    }

    private static double GetSystemUsedRamMbLinux()
    {
        try
        {
            if (!OperatingSystem.IsLinux())
                return 0;
            
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

    private static (long sent, long recv) GetNetworkBytes()
    {
        long sent = 0, receive = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var stats = ni.GetIPv4Statistics();
                sent += stats.BytesSent;
                receive += stats.BytesReceived;
            }
        }
        catch
        {
            // ignored
        }

        return (sent, receive);
    }

    private static (long read, long write) GetDiskBytes()
    {
        return OperatingSystem.IsWindows() ? GetDiskBytesWindows() : GetDiskBytesLinux();
    }

    private static (long read, long write) GetDiskBytesLinux()
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
                read += long.Parse(parts[5]) * 512;
                write += long.Parse(parts[9]) * 512;
            }
        }
        catch
        {
            // ignored
        }

        return (read, write);
    }

    private static (long read, long write) GetDiskBytesWindows()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return (0, 0);
            
            using var readCounter = new PerformanceCounter(
                "PhysicalDisk",
                "Disk Read Bytes/sec",
                "_Total");
            
            using var writeCounter = new PerformanceCounter(
                "PhysicalDisk",
                "Disk Write Bytes/sec",
                "_Total");

            return (readCounter.RawValue, writeCounter.RawValue);
        }
        catch
        {
            return (0, 0);
        }
    }

    public void Dispose()
    {
        _ = ShutdownAsync(CancellationToken.None);
        GC.SuppressFinalize(this);
    }
}


/*
┌──────────────────────────────────────────────┐
│ Through me the way is to the city dolent;    │
│ Through me the way is to the eternal dolor;  │
│ Through me the way is to the race condemned. │
│                                              │
│ Abandon all hope, ye who enter here.         │
└──────────────────────────────────────────────┘
*/


/// <summary>
/// Contract for CPU temperature readers
/// </summary>
public interface ICpuTemperatureReader
{
    /// <summary>
    /// Reads the current CPU temperature in Celsius
    /// </summary>
    double? Read();
}

/// <summary>
/// Cross-platform CPU temperature reader
/// </summary>
public sealed class CpuTemperatureReader(ILogger<DeviceStatsService>? logger = null)
{
    private static readonly TimeSpan MinReadInterval = TimeSpan.FromSeconds(1);

    private readonly ICpuTemperatureReader _platformReader = CreatePlatformReader(logger);
    
    private double? _cachedValue;
    private DateTime _lastReadTime;

    private static ICpuTemperatureReader CreatePlatformReader(ILogger<DeviceStatsService>? logger)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCpuTemperatureReader(logger);
        }

        return new LinuxCpuTemperatureReader(logger);
    }

    public double? Read()
    {
        // Return cached value if read too recently
        if (_cachedValue.HasValue && 
            DateTime.UtcNow - _lastReadTime < MinReadInterval)
        {
            return _cachedValue;
        }

        try
        {
            var temperature = _platformReader.Read();
            _cachedValue = temperature;
            _lastReadTime = DateTime.UtcNow;
            
            return temperature;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "failed to read cpu temp");
            _cachedValue = null;
            return null;
        }
    }
}

internal sealed partial class LinuxCpuTemperatureReader(ILogger? logger) : ICpuTemperatureReader
{
    private static readonly string[] KnownCpuDrivers =
        ["k10temp", "coretemp", "cpu_thermal", "acpitz", "k8temp", "zenpower"];

    public double? Read()
    {
        // s0: hwmon
        var temp = TryReadHwmon();
        if (temp.HasValue) return temp;

        // s1: acpi
        temp = TryReadThermalZones();
        if (temp.HasValue) return temp;

        // s2: fallback to sensors cli
        return TryReadViaSensorsCli();
    }

    private static double? TryReadHwmon()
    {
        const string hwmonPath = "/sys/class/hwmon";
        
        if (!Directory.Exists(hwmonPath))
        {
            return null;
        }

        double? cpuTemp = null;

        foreach (var hwmonDir in Directory.GetDirectories(hwmonPath))
        {
            var nameFile = Path.Combine(hwmonDir, "name");
            if (!File.Exists(nameFile))
                continue;

            var driverName = File.ReadAllText(nameFile).Trim();
            if (!KnownCpuDrivers.Contains(driverName))
                continue;

            // temp1_input
            for (var i = 1; i <= 8; i++)
            {
                var inputFile = Path.Combine(hwmonDir, $"temp{i}_input");
                if (!File.Exists(inputFile)) continue;

                var labelFile = Path.Combine(hwmonDir, $"temp{i}_label");
                var label = "";
                
                if (File.Exists(labelFile))
                {
                    label = File.ReadAllText(labelFile).Trim().ToLowerInvariant();
                }

                if (!string.IsNullOrEmpty(label))
                {
                    var isCpuLabel = label.Contains("core") || 
                                     label.Contains("package") ||
                                     label.Contains("tdie") ||
                                     label.Contains("tctl");
                    
                    if (!isCpuLabel && i > 1) continue;
                }

                if (File.ReadAllText(inputFile).Trim() is not { } raw ||
                    !int.TryParse(raw, out var milli) || milli <= 0)
                    continue;
                
                var celsius = milli / 1000.0;
                
                // validate (if you cool your processor with
                // liquid nitrogen, then use a different software
                // to check the processor temperature)
                if (celsius is < 20 or > 100)
                    continue;
                
                cpuTemp = celsius;
                return cpuTemp;
            }
        }

        return cpuTemp;
    }

    private static double? TryReadThermalZones()
    {
        const string thermalPath = "/sys/class/thermal";
        
        if (!Directory.Exists(thermalPath))
        {
            return null;
        }

        foreach (var zone in Directory.GetDirectories(thermalPath, "thermal_zone*"))
        {
            var typeFile = Path.Combine(zone, "type");
            var tempFile = Path.Combine(zone, "temp");
            
            if (!File.Exists(tempFile)) continue;

            if (!int.TryParse(File.ReadAllText(tempFile).Trim(), out int milli))
                continue;

            var celsius = milli / 1000.0;

            // validate temperature (if you cool your processor with
            // liquid nitrogen bla bla bla)
            if (celsius is < 20 or > 100)
                continue;
            
            // 16.8°C is a common fake value
            if (Math.Abs(celsius - 16.8) < 0.5)
                continue;

            var type = "";
            if (File.Exists(typeFile))
            {
                type = File.ReadAllText(typeFile).Trim().ToLowerInvariant();
            }

            // prefer CPU-related zones
            if (type.Contains("cpu") || type.Contains("package") || type.Contains("x86"))
            {
                return celsius;
            }
        }

        return null;
    }

    private static double? TryReadViaSensorsCli()
    {
        try
        {
            var psi = new ProcessStartInfo("sensors", "-A")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            // parse
            var regex = LinesRegex();

            foreach (Match m in regex.Matches(output))
            {
                if (double.TryParse(m.Groups["temp"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var t) && t is >= 20 and <= 100)
                {
                    return t;
                }
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    [GeneratedRegex(@"^(?<label>Core\s*\d+|Tdie|Tctl|Package|CPU):\s+\+?(?<temp>\d+\.\d+)\s*°?C", RegexOptions.IgnoreCase | RegexOptions.Multiline, "ru-RU")]
    private static partial Regex LinesRegex();
}

internal sealed class WindowsCpuTemperatureReader : ICpuTemperatureReader, IDisposable
{
    private readonly ILogger? _logger;
    private readonly Computer? _lhmComputer;
    private readonly bool _lhmStarted;
    private readonly bool _canGetTemp = true;

    public WindowsCpuTemperatureReader(ILogger? logger)
    {
        _logger = logger;
        
        var sw = Stopwatch.StartNew();
        try
        {
            _lhmComputer = new Computer
            {
                IsCpuEnabled = true,
                IsMotherboardEnabled = true,
                IsGpuEnabled = false,
                IsMemoryEnabled = false,
                IsNetworkEnabled = false,
                IsStorageEnabled = false
            };
            _lhmComputer.Open();
            _lhmStarted = true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[cpu:temp] lhm init failed");
        }

        logger?.LogInformation("[cpu:temp] lhm open took {Ms}ms", sw.ElapsedMilliseconds);
        sw.Restart();

        try
        {
            var result = Read();
            if (result is null or 0)
            {
                _canGetTemp = false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[cpu:temp] temperature read failed");
        }

        logger?.LogInformation("[cpu:temp] initial read took {Ms}ms", sw.ElapsedMilliseconds);
    }

    public double? Read()
    {
        if (!_canGetTemp) return 0;
        
        // s0: wmi
        var temp = TryReadWmiAcpi();
        if (temp.HasValue) return temp;

        // s1: performance counters
        temp = TryReadPerformanceCounter();
        if (temp.HasValue) return temp;

        // s2: wmi probe
        temp = TryReadWmiProbe();
        if (temp.HasValue) return temp;

        // s3: libre
        return TryReadLibreHardwareMonitor();
    }
    
    private static double? TryReadWmiAcpi()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return 0;
            
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
            foreach (var obj in searcher.Get())
            {
                var raw = Convert.ToDouble(obj["CurrentTemperature"]);
                var celsius = (raw / 10.0) - 273.15;
                if (celsius is > 10 and < 110) return celsius;
            }
        }
        catch
        {
            //
        }
        return null;
    }

    private static double? TryReadPerformanceCounter()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return 0;
            
            using var pc = new PerformanceCounter(
                "Thermal Zone Information",
                "Temperature",
                @"\_TZ.THM0",
                true);
            var kelvin = pc.NextValue();
            if (kelvin <= 0) return null;
            
            return kelvin - 273.15;
        }
        catch 
        { 
            return null;
        }
    }

    private static double? TryReadWmiProbe()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return 0;
            
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_TemperatureProbe");
            foreach (var obj in searcher.Get())
            {
                var val = obj["CurrentReading"];
                if (val != null) return Convert.ToDouble(val);
            }
        }
        catch 
        { 
            //
        }
        return null;
    }

    private double TryReadLibreHardwareMonitor()
    {
        if (!_lhmStarted || _lhmComputer == null) return 0;

        try
        {
            foreach (var hardware in _lhmComputer.Hardware)
            {
                if (hardware.HardwareType != HardwareType.Cpu)
                    continue;
                hardware.Update();
                    
                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType !=
                        SensorType.Temperature ||
                        (!sensor.Name.Contains("Core") &&
                         !sensor.Name.Contains("Package") &&
                         !sensor.Name.Contains("Tdie")))
                        continue;
                    
                    if (sensor.Value is { } value and >= 20 and <= 100)
                    {
                        return value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "librehardwaremonitor read failed");
        }

        return 0;
    }

    public void Dispose()
    {
        try
        {
            _lhmComputer?.Close();
        }
        catch
        { 
            //
        }
    }
}