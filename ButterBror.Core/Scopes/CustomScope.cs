using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ButterBror.Core.Scopes;

/// <summary>
/// Encapsulates stop logging and timing logic
/// </summary>
public readonly struct CustomScope : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly Stopwatch _stopwatch;
    private readonly string _scopeName;
    private readonly bool _isMain;
    private readonly string _type;

    public CustomScope(ILogger logger, string scopeType, string scopeName, bool isMain = false)
    {
        _logger = logger;
        _scopeName = scopeName;
        _stopwatch = Stopwatch.StartNew();
        _isMain = isMain;
        _type = scopeType;

        var message = _isMain ? "><> [{Type}] {ScopeName}" : "[{Type}] {ScopeName}";
        _logger.LogInformation(message, _type, _scopeName);
    }

    public ValueTask DisposeAsync()
    {
        _stopwatch.Stop();

        var message = _isMain ? "><> [{Type}:ok] {ScopeName} in {Time}ms" : "[{Type}:ok] {ScopeName} in {Time}ms";
        _logger.LogInformation(message, _type, _scopeName, _stopwatch.ElapsedMilliseconds);
        
        return ValueTask.CompletedTask;
    }
}