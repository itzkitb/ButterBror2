using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using Microsoft.Extensions.Logging;

namespace ButterBror.Host.Logging;

/// <summary>
/// Logger provider that forwards log entries to the Dashboard bridge
/// </summary>
public class DashboardLoggerProvider(IDashboardBridge bridge) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new DashboardLogger(categoryName, bridge);

    public void Dispose() { }
}

public class DashboardLogger(string category, IDashboardBridge bridge) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;
        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null)
            return;
        
        if (category.StartsWith("ButterBror.Dashboard", StringComparison.Ordinal))
            return;

        bridge.PushLog(new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = logLevel.ToString(),
            Category = category,
            Message = message,
            Exception = exception?.ToString()
        });
    }
}
