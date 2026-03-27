namespace ButterBror.Core.Interfaces;

/// <summary>
/// Service for obtaining information about hardware
/// </summary>
public interface IDeviceStatsService
{
    /// <summary>
    /// Current CPU load
    /// </summary>
    double CpuLoad { get; }

    /// <summary>
    /// CPU temperature in degrees Celsius
    /// </summary>
    double CpuTemperature { get; }

    /// <summary>
    /// Amount of free memory in megabytes
    /// </summary>
    double TotalMemory { get; }

    /// <summary>
    /// Amount of memory used in megabytes
    /// </summary>
    double MemoryUsed { get; }

    /// <summary>
    /// Network download speed in megabytes per second
    /// </summary>
    double NetworkIn { get; }

    /// <summary>
    /// Network upload speed in megabytes per second
    /// </summary>
    double NetworkOut { get; }

    /// <summary>
    /// Disk write speed in megabytes per second
    /// </summary>
    double DiskIn { get; }

    /// <summary>
    /// Disk read speed in megabytes per second
    /// </summary>
    double DiskOut { get; }
}