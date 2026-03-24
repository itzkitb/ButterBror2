using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using Microsoft.Extensions.Logging;

namespace ButterBror.Host.Logging;

/// <summary>
/// Logger provider that forwards log entries to the Dashboard bridge
/// </summary>
public class DashboardLoggerProvider : ILoggerProvider
{
    private readonly IDashboardBridge _bridge;

    public DashboardLoggerProvider(IDashboardBridge bridge) => _bridge = bridge;

    public ILogger CreateLogger(string categoryName) =>
        new DashboardLogger(categoryName, _bridge);

    public void Dispose() { }
}

public class DashboardLogger : ILogger
{
    private readonly string _category;
    private readonly IDashboardBridge _bridge;

    public DashboardLogger(string category, IDashboardBridge bridge)
    {
        _category = category;
        _bridge = bridge;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message)) return;

        // Skip dashboard own logs
        if (_category.StartsWith("ButterBror.Dashboard", StringComparison.Ordinal))
            return;

        _bridge.PushLog(new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = logLevel.ToString(),
            Category = _category,
            Message = message,
            Exception = exception?.ToString()
        });
    }
}
