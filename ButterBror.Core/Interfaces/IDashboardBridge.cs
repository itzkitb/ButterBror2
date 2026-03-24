using ButterBror.Core.Models;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Bridge between the bot infrastructure and the dashboard
/// </summary>
public interface IDashboardBridge
{
    /// <summary>
    /// Push a log entry to connected dashboard clients
    /// </summary>
    void PushLog(LogEntry entry);

    /// <summary>
    /// Increment the processed messages counter (called by chat modules)
    /// </summary>
    void IncrementMessageCount();

    /// <summary>
    /// Increment the executed commands counter
    /// </summary>
    void IncrementCommandCount();

    /// <summary>
    /// Read the most recent N log entries (for initial page load)
    /// </summary>
    IReadOnlyList<LogEntry> GetRecentLogs(int count = 200);

    /// <summary>
    /// Get the current commands-per-minute rate
    /// </summary>
    double GetCommandsPerMinute();

    /// <summary>
    /// Get the current messages-per-minute rate
    /// </summary>
    double GetMessagesPerMinute();
}
