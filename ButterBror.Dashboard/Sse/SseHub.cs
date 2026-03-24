using System.Collections.Concurrent;
using System.Text.Json;
using ButterBror.Core.Models;
using ButterBror.Dashboard.Models;

namespace ButterBror.Dashboard.Sse;

/// <summary>
/// Manages all active SSE connections and broadcasts events to them
/// </summary>
public class SseHub
{
    private readonly ConcurrentDictionary<string, SseConnection> _connections = new();

    public void Add(SseConnection conn) =>
        _connections[conn.Id] = conn;

    public void Remove(string id) =>
        _connections.TryRemove(id, out _);

    public async Task BroadcastMetricsAsync(MetricsSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot);
        await BroadcastAsync("metrics", json, "metrics");
    }

    public async Task BroadcastLogAsync(LogEntry entry)
    {
        var json = JsonSerializer.Serialize(entry);
        await BroadcastAsync("log", json, "logs");
    }

    private async Task BroadcastAsync(string eventName, string json, string type)
    {
        var dead = new List<string>();
        foreach (var (id, conn) in _connections)
        {
            if (conn.Type != type) continue;
            var ok = await conn.SendAsync(eventName, json);
            if (!ok) dead.Add(id);
        }
        foreach (var id in dead)
            Remove(id);
    }
}
