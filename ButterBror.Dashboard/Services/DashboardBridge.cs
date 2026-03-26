using System.Collections.Concurrent;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using ButterBror.Dashboard.Models;
using Microsoft.Extensions.Options;

namespace ButterBror.Dashboard.Services;

/// <summary>
/// Bridge between the bot infrastructure and the dashboard
/// </summary>
public class DashboardBridge : IDashboardBridge
{
    private readonly DashboardOptions _options;
    private readonly ConcurrentQueue<LogEntry> _logBuffer = new();
    private readonly IBotStatsService _stats;

    // Callbacks registered by SseHub
    public event Action<LogEntry>? OnLogEntry;
    public event Action<MetricsSnapshot>? OnMetrics;

    public DashboardBridge(
        IOptions<DashboardOptions> options,
        IBotStatsService stats)
    {
        _options = options.Value;
        _stats = stats;
    }

    public void PushLog(LogEntry entry)
    {
        _logBuffer.Enqueue(entry);
        while (_logBuffer.Count > _options.MaxLogBufferSize)
            _logBuffer.TryDequeue(out _);
        OnLogEntry?.Invoke(entry);
    }

    public void IncrementCommandCount()
    {
        _stats.IncrementCommandCount();
    }

    public void IncrementMessageCount()
    {
        _stats.IncrementMessageCount();
    }

    public double GetCommandsPerMinute()
    {
        return _stats.CommandsPerMinute;
    }

    public double GetMessagesPerMinute()
    {
        return _stats.MessagesPerMinute;
    }

    public IReadOnlyList<LogEntry> GetRecentLogs(int count = 200) =>
        _logBuffer.TakeLast(count).ToList();
}
