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

    // Rolling minute counters
    private readonly Queue<(DateTime At, int Count)> _commandTicks = new();
    private readonly Queue<(DateTime At, int Count)> _messageTicks = new();
    private readonly object _tickLock = new();

    // Callbacks registered by SseHub
    public event Action<LogEntry>? OnLogEntry;
    public event Action<MetricsSnapshot>? OnMetrics;

    public DashboardBridge(IOptions<DashboardOptions> options)
    {
        _options = options.Value;
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
        lock (_tickLock)
            _commandTicks.Enqueue((DateTime.UtcNow, 1));
    }

    public void IncrementMessageCount()
    {
        lock (_tickLock)
            _messageTicks.Enqueue((DateTime.UtcNow, 1));
    }

    public double GetCommandsPerMinute()
    {
        lock (_tickLock)
        {
            PruneOldTicks(_commandTicks);
            return _commandTicks.Sum(t => t.Count);
        }
    }

    public double GetMessagesPerMinute()
    {
        lock (_tickLock)
        {
            PruneOldTicks(_messageTicks);
            return _messageTicks.Sum(t => t.Count);
        }
    }

    public IReadOnlyList<LogEntry> GetRecentLogs(int count = 200) =>
        _logBuffer.TakeLast(count).ToList();

    private static void PruneOldTicks(Queue<(DateTime At, int Count)> queue)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-1);
        while (queue.TryPeek(out var head) && head.At < cutoff)
            queue.Dequeue();
    }
}
