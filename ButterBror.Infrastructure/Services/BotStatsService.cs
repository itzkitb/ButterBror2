using System.Text.Json;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

/// <summary>
/// Bot statistics service providing live metrics and persistent counters
/// </summary>
public class BotStatsService : IBotStatsService
{
    private readonly IAppDataPathProvider _pathProvider;
    private readonly ILogger<BotStatsService> _logger;

    // Minute counters for CpM/MpM
    private readonly Queue<(DateTime At, int Count)> _commandTicks = new();
    private readonly Queue<(DateTime At, int Count)> _messageTicks = new();
    private readonly object _tickLock = new();

    // Redis ops rolling windows
    private readonly Queue<(DateTime At, long Ops)> _opsMinQueue = new();
    private readonly Queue<(DateTime At, long Ops)> _opsHourQueue = new();
    private readonly object _opsLock = new();

    // Redis live stats
    private long _redisMemoryUsedBytes;
    private long _redisConnectedClients;
    private long _redisOpsPerSecond;
    private readonly object _redisLock = new();

    // Session tracking
    private DateTime _startedAt;

    // Persistent stats
    private PersistentBotStats _persistent = new();
    private long _commandsAtStart;
    private long _repliesAtStart;
    private TimeSpan _uptimeAtStart;

    // In-memory counters
    private long _currentSessionCommands;
    private long _currentSessionReplies;

    // Flush timer
    private Timer? _flushTimer;
    private readonly SemaphoreSlim _flushLock = new(1, 1);

    private bool _initialized;

    public BotStatsService(
        IAppDataPathProvider pathProvider,
        ILogger<BotStatsService> logger)
    {
        _pathProvider = pathProvider;
        _logger = logger;
        _startedAt = DateTime.UtcNow;
    }

    // Live

    public double CommandsPerMinute
    {
        get
        {
            lock (_tickLock)
            {
                PruneOldTicks(_commandTicks);
                return _commandTicks.Sum(t => t.Count);
            }
        }
    }

    public double MessagesPerMinute
    {
        get
        {
            lock (_tickLock)
            {
                PruneOldTicks(_messageTicks);
                return _messageTicks.Sum(t => t.Count);
            }
        }
    }

    // Redis

    public long RedisMemoryUsedBytes
    {
        get
        {
            lock (_redisLock)
                return _redisMemoryUsedBytes;
        }
    }

    public long RedisConnectedClients
    {
        get
        {
            lock (_redisLock)
                return _redisConnectedClients;
        }
    }

    public long RedisOpsPerSecond
    {
        get
        {
            lock (_redisLock)
                return _redisOpsPerSecond;
        }
    }

    public long RedisOpsPerMinute
    {
        get
        {
            lock (_opsLock)
            {
                PruneOldOps(_opsMinQueue, TimeSpan.FromMinutes(1));
                return _opsMinQueue.Sum(t => t.Ops);
            }
        }
    }

    public long RedisOpsPerHour
    {
        get
        {
            lock (_opsLock)
            {
                PruneOldOps(_opsHourQueue, TimeSpan.FromHours(1));
                return _opsHourQueue.Sum(t => t.Ops);
            }
        }
    }

    // Uptime

    public TimeSpan CurrentSessionUptime => DateTime.UtcNow - _startedAt;

    public TimeSpan TotalUptime => _persistent.TotalUptime + CurrentSessionUptime;

    // Persistent

    public long TotalCommandsExecuted => _persistent.TotalCommandsExecuted + _currentSessionCommands;

    public long TotalRepliesSent => _persistent.TotalRepliesSent + _currentSessionReplies;

    // Methods

    public void IncrementCommandCount()
    {
        Interlocked.Increment(ref _currentSessionCommands);
        lock (_tickLock)
            _commandTicks.Enqueue((DateTime.UtcNow, 1));
    }

    public void IncrementMessageCount()
    {
        lock (_tickLock)
            _messageTicks.Enqueue((DateTime.UtcNow, 1));
    }

    public void IncrementRepliesCount()
    {
        Interlocked.Increment(ref _currentSessionReplies);
    }

    public void UpdateRedisStats(long memoryUsedBytes, long connectedClients, long opsPerSecond)
    {
        lock (_redisLock)
        {
            _redisMemoryUsedBytes = memoryUsedBytes;
            _redisConnectedClients = connectedClients;
            _redisOpsPerSecond = opsPerSecond;
        }

        lock (_opsLock)
        {
            var now = DateTime.UtcNow;
            _opsMinQueue.Enqueue((now, opsPerSecond));
            _opsHourQueue.Enqueue((now, opsPerSecond));
        }
    }

    // Inititialize

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        _startedAt = DateTime.UtcNow;

        var statsPath = GetStatsFilePath();
        var directory = Path.GetDirectoryName(statsPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _logger.LogDebug("Created directory {Directory}", directory);
        }

        if (File.Exists(statsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(statsPath, cancellationToken);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                _persistent = JsonSerializer.Deserialize<PersistentBotStats>(json, options) ?? new PersistentBotStats();
                _logger.LogInformation("Loaded stats from {Path}", statsPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load stats, starting with defaults");
                _persistent = new PersistentBotStats();
            }
        }
        else
        {
            _logger.LogInformation("No persistent stats found, starting with defaults");
            _persistent = new PersistentBotStats();
        }

        _commandsAtStart = _persistent.TotalCommandsExecuted;
        _repliesAtStart = _persistent.TotalRepliesSent;
        _uptimeAtStart = _persistent.TotalUptime;

        _flushTimer = new Timer(OnFlushTimer, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        _initialized = true;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
            return;

        await _flushLock.WaitAsync(cancellationToken);
        try
        {
            var statsPath = GetStatsFilePath();
            var directory = Path.GetDirectoryName(statsPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _persistent.TotalCommandsExecuted = _commandsAtStart + _currentSessionCommands;
            _persistent.TotalRepliesSent = _repliesAtStart + _currentSessionReplies;
            _persistent.TotalUptime = _uptimeAtStart + CurrentSessionUptime;

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var json = JsonSerializer.Serialize(_persistent, options);

            await File.WriteAllTextAsync(statsPath, json, cancellationToken);
            _logger.LogDebug("Flushed stats to {Path}", statsPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush stats");
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private void OnFlushTimer(object? state)
    {
        _ = FlushAsync(CancellationToken.None);
    }

    private string GetStatsFilePath()
    {
        var appDataPath = _pathProvider.GetAppDataPath();
        return Path.Combine(appDataPath, "Stats.json");
    }

    private static void PruneOldTicks(Queue<(DateTime At, int Count)> queue)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-1);
        while (queue.TryPeek(out var head) && head.At < cutoff)
            queue.Dequeue();
    }

    private static void PruneOldOps(Queue<(DateTime At, long Ops)> queue, TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        while (queue.TryPeek(out var head) && head.At < cutoff)
            queue.Dequeue();
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        _flushLock.Dispose();
    }
}
