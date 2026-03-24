namespace ButterBror.Dashboard.Sse;

/// <summary>
/// Represents a single SSE connection to a dashboard client
/// </summary>
public class SseConnection : IDisposable
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Type { get; }
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public SseConnection(string type, StreamWriter writer)
    {
        Type = type;
        _writer = writer;
    }

    public async Task<bool> SendAsync(string eventName, string jsonData)
    {
        if (_disposed) return false;
        await _lock.WaitAsync();
        try
        {
            await _writer.WriteAsync($"event: {eventName}\n");
            await _writer.WriteAsync($"data: {jsonData}\n\n");
            await _writer.FlushAsync();
            return true;
        }
        catch
        {
            _disposed = true;
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _disposed = true;
}
