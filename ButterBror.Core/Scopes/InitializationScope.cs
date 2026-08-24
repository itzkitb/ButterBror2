using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ButterBror.Core.Scopes;

/// <summary>
/// Encapsulates initialization logging and timing logic
/// </summary>
public readonly struct InitializationScope : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly Stopwatch _stopwatch;
    private readonly string _scopeName;
    private readonly bool _isMain;

    public InitializationScope(ILogger logger, string scopeName, bool isMain = false)
    {
        _logger = logger;
        _scopeName = scopeName;
        _stopwatch = Stopwatch.StartNew();
        _isMain = isMain;

        var message = _isMain ? "><> [init] {ScopeName}" : "[init] {ScopeName}";
        _logger.LogInformation(message, _scopeName);
    }

    public ValueTask DisposeAsync()
    {
        _stopwatch.Stop();

        var message = _isMain ? "><> [init:ok] {ScopeName} in {Time}ms" : "[init:ok] {ScopeName} in {Time}ms";
        _logger.LogInformation(message, _scopeName, _stopwatch.ElapsedMilliseconds);
        
        return ValueTask.CompletedTask;
    }
}