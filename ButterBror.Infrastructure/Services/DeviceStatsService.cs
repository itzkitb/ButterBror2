using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Globalization;
using LibreHardwareMonitor.Hardware;
using Polly.Fallback;

namespace ButterBror.Infrastructure.Services;

public class DeviceStatsService : IDeviceStatsService, IDisposable
{
    private ILogger<DeviceStatsService> _logger;
    private readonly CpuTemperatureReader _cpuTempReader;

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
        _cpuTempReader = new CpuTemperatureReader(logger);
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
                _cpuTemp = _cpuTempReader.Read() ?? 0;

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

// ┌──────────────────────────────────────────────┐
// │ Through me the way is to the city dolent;    │
// │ Through me the way is to the eternal dolour; │
// │ Through me the way is to the race condemned. │
// │                                              │
// │ Abandon all hope, ye who enter here.         │
// └──────────────────────────────────────────────┘

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
public sealed class CpuTemperatureReader
{

    private static readonly TimeSpan MinReadInterval = TimeSpan.FromSeconds(1);

    private readonly ILogger<DeviceStatsService>? _logger;
    private readonly ICpuTemperatureReader _platformReader;
    
    private double? _cachedValue;
    private DateTime _lastReadTime;

    public CpuTemperatureReader(ILogger<DeviceStatsService>? logger = null)
    {
        _logger = logger;
        _platformReader = CreatePlatformReader(logger);
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
            
            _logger?.LogDebug("CPU temperature read: {Temperature}°C", temperature);
            return temperature;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to read CPU temperature");
            _cachedValue = null;
            return null;
        }
    }

    private static ICpuTemperatureReader CreatePlatformReader(
        ILogger<DeviceStatsService>? logger)
    {
        return OperatingSystem.IsWindows()
            ? new WindowsCpuTemperatureReader(logger)
            : new LinuxCpuTemperatureReader(logger);
    }
}

// Linux
internal sealed class LinuxCpuTemperatureReader : ICpuTemperatureReader
{
    private static readonly string[] KnownCpuDrivers =
        ["k10temp", "coretemp", "cpu_thermal", "acpitz", "k8temp", "zenpower"];

    private readonly ILogger? _logger;

    public LinuxCpuTemperatureReader(ILogger? logger)
    {
        _logger = logger;
    }

    public double? Read()
    {
        // S0. hwmon
        var temp = TryReadHwmon();
        if (temp.HasValue) return temp;

        // S1. ACPI
        temp = TryReadThermalZones();
        if (temp.HasValue) return temp;

        // S2. Fallback to sensors CLI
        return TryReadViaSensorsCli();
    }

    private double? TryReadHwmon()
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
            if (!File.Exists(nameFile)) continue;

            var driverName = File.ReadAllText(nameFile).Trim();
            if (!KnownCpuDrivers.Contains(driverName)) continue;

            // Read temp1_input
            for (int i = 1; i <= 8; i++)
            {
                var inputFile = Path.Combine(hwmonDir, $"temp{i}_input");
                if (!File.Exists(inputFile)) continue;

                var labelFile = Path.Combine(hwmonDir, $"temp{i}_label");
                string label = "";
                
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

                if (File.ReadAllText(inputFile).Trim() is { } raw &&
                    int.TryParse(raw, out int milli) && milli > 0)
                {
                    var celsius = milli / 1000.0;
                    
                    // Validate (If you cool your processor with
                    // liquid nitrogen, then use a different software
                    // to check the processor temperature)
                    if (celsius is >= 20 and <= 100)
                    {
                        cpuTemp = celsius;
                        _logger?.LogDebug(
                            "Read CPU temp from hwmon {Driver}/{Label}: {Temp}°C",
                            driverName, label, celsius);
                        return cpuTemp;
                    }
                }
            }
        }

        return cpuTemp;
    }

    private double? TryReadThermalZones()
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

            // Validate temperature (If you cool your processor with
            // liquid nitrogen, then use a different software
            // to check the processor temperature)
            if (celsius is < 20 or > 100) continue;
            
            // 16.8°C is a common fake value
            if (Math.Abs(celsius - 16.8) < 0.5) continue;

            string type = "";
            if (File.Exists(typeFile))
            {
                type = File.ReadAllText(typeFile).Trim().ToLowerInvariant();
            }

            // Prefer CPU-related zones
            if (type.Contains("cpu") || type.Contains("package") || type.Contains("x86"))
            {
                _logger?.LogDebug(
                    "Read CPU temp from thermal zone {Type}: {Temp}°C",
                    type, celsius);
                return celsius;
            }
        }

        return null;
    }

    private double? TryReadViaSensorsCli()
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

            // Parse lines
            var regex = new Regex(
                @"^(?<label>Core\s*\d+|Tdie|Tctl|Package|CPU):\s+\+?(?<temp>\d+\.\d+)\s*°?C",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            foreach (Match m in regex.Matches(output))
            {
                if (double.TryParse(m.Groups["temp"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double t) && t is >= 20 and <= 100)
                {
                    _logger?.LogDebug("Read CPU temp from sensors CLI: {Temp}°C", t);
                    return t;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "sensors CLI not available");
        }

        return null;
    }
}

// Windows
internal sealed class WindowsCpuTemperatureReader : ICpuTemperatureReader, IDisposable
{
    private readonly ILogger? _logger;
    private readonly Computer? _lhmComputer;
    private bool _lhmStarted = false;
    private bool _canGetTemp = true;

    public WindowsCpuTemperatureReader(ILogger? logger)
    {
        _logger = logger;

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
            _logger?.LogWarning(ex, "LHM initialization failed");
        }

        try
        {
            var result = Read();
            if (!result.HasValue || result == 0)
            {
                _canGetTemp = false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Temperature read failed");
        }
    }

    public double? Read()
    {
        if (!_canGetTemp) return 0;

        // S0. WMI
        var temp = TryReadWmiAcpi();
        if (temp.HasValue) return temp;

        // S1. Performance Counters
        temp = TryReadPerformanceCounter();
        if (temp.HasValue) return temp;

        // S2. WMI Probe
        temp = TryReadWmiProbe();
        if (temp.HasValue) return temp;

        // S3. LibreHardwareMonitor
        return TryReadLibreHardwareMonitor();
    }

    private double? TryReadWmiAcpi()
    {
        try
        {
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

    private double? TryReadPerformanceCounter()
    {
        try
        {
            using var pc = new PerformanceCounter("Thermal Zone Information", "Temperature", @"\_TZ.THM0", true);
            float kelvin = pc.NextValue();
            if (kelvin <= 0) return null;
            
            return kelvin - 273.15;
        }
        catch 
        { 
            return null;
        }
    }

    private double? TryReadWmiProbe()
    {
        try
        {
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
        if (!_lhmStarted) return 0;

        try
        {
            foreach (var hardware in _lhmComputer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    hardware.Update();
                    
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == 
                            SensorType.Temperature &&
                            sensor.Name.Contains("Core") || 
                            sensor.Name.Contains("Package") ||
                            sensor.Name.Contains("Tdie"))
                        {
                            if (sensor.Value is float value && value is >= 20 and <= 100)
                            {
                                return value;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "LibreHardwareMonitor read failed");
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