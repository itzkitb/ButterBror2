using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ButterBror.Core.Scopes;

/// <summary>
/// Encapsulates stop logging and timing logic
/// </summary>
public readonly struct StopScope : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly Stopwatch _stopwatch;
    private readonly string _scopeName;
    private readonly bool _isMain;

    public StopScope(ILogger logger, string scopeName, bool isMain = false)
    {
        _logger = logger;
        _scopeName = scopeName;
        _stopwatch = Stopwatch.StartNew();
        _isMain = isMain;

        var message = _isMain ? "><> [stop] {ScopeName}" : "[stop] {ScopeName}";
        _logger.LogInformation(message, _scopeName);
    }

    public ValueTask DisposeAsync()
    {
        _stopwatch.Stop();

        var message = _isMain ? "><> [stop:ok] {ScopeName} in {Time}ms" : "[stop:ok] {ScopeName} in {Time}ms";
        _logger.LogInformation(message, _scopeName, _stopwatch.ElapsedMilliseconds);
        
        return ValueTask.CompletedTask;
    }
}