using System.Text.Json;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using ButterBror.Core.Scopes;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

/// <summary>
/// Bot statistics service providing live metrics and persistent counters
/// </summary>
public class BotStatsService(
    IAppDataPathProvider pathProvider,
    ILogger<BotStatsService> logger,
    JsonSerializerOptions jsonOptions)
    : IBotStatsService
{
    // ><> private
    // ^ minute counters for cpc/mpm
    private readonly Queue<(DateTime At, int Count)> _commandTicks = new();
    private readonly Queue<(DateTime At, int Count)> _messageTicks = new();
    private readonly Lock _tickLock = new();

    // ^ db ops rolling
    private readonly Queue<(DateTime At, long Ops)> _opsMinQueue = new();
    private readonly Queue<(DateTime At, long Ops)> _opsHourQueue = new();
    private readonly Lock _opsLock = new();

    // ^ db live stats
    private long _redisMemoryUsedBytes;
    private long _redisConnectedClients;
    private long _redisOpsPerSecond;
    private long _redisKeys;
    private readonly Lock _redisLock = new();

    // ^ session tracking
    private DateTime _startedAt = DateTime.UtcNow;

    // ^ persistent stats
    private PersistentBotStats _persistent = new();
    private long _commandsAtStart;
    private long _repliesAtStart;
    private TimeSpan _uptimeAtStart;

    // ^ in-memory counters
    private long _currentSessionCommands;
    private long _currentSessionReplies;

    // ^ flush timer
    private Timer? _flushTimer;
    private readonly SemaphoreSlim _flushLock = new(1, 1);

    private bool _initialized;

    // ><> public

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

    // ^ db

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

    public long RedisTotalKeys
    {
        get
        {
            lock (_redisLock)
                return _redisKeys;
        }
    }
    
    // ^ uptime

    public TimeSpan CurrentSessionUptime => DateTime.UtcNow - _startedAt;

    public TimeSpan TotalUptime => _persistent.TotalUptime + CurrentSessionUptime;

    // ^ persistent

    public long TotalCommandsExecuted => _persistent.TotalCommandsExecuted + _currentSessionCommands;

    public long TotalRepliesSent => _persistent.TotalRepliesSent + _currentSessionReplies;

    // ><> methods

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

    public void UpdateRedisStats(long memoryUsedBytes, long connectedClients, long opsPerSecond, long keys)
    {
        lock (_redisLock)
        {
            _redisMemoryUsedBytes = memoryUsedBytes;
            _redisConnectedClients = connectedClients;
            _redisOpsPerSecond = opsPerSecond;
            _redisKeys = keys;
        }

        lock (_opsLock)
        {
            var now = DateTime.UtcNow;
            _opsMinQueue.Enqueue((now, opsPerSecond));
            _opsHourQueue.Enqueue((now, opsPerSecond));
        }
    }

    // ><> init

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        await using var _ = new InitializationScope(logger, "bot statistics");
        
        _startedAt = DateTime.UtcNow;

        var statsPath = GetStatsFilePath();
        var directory = Path.GetDirectoryName(statsPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            logger.LogDebug("created directory. path='{Directory}'", directory);
        }

        if (File.Exists(statsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(statsPath, cancellationToken);
                _persistent = JsonSerializer.Deserialize<PersistentBotStats>(json, jsonOptions) ?? new PersistentBotStats();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "failed to load stats, starting with defaults. path='{Path}', message='{Message}'", statsPath, ex.Message);
                _persistent = new PersistentBotStats();
            }
        }
        else
        {
            logger.LogInformation("no persistent stats found, starting with defaults. path='{Path}'", statsPath);
            _persistent = new PersistentBotStats();
        }

        _commandsAtStart = _persistent.TotalCommandsExecuted;
        _repliesAtStart = _persistent.TotalRepliesSent;
        _uptimeAtStart = _persistent.TotalUptime;

        _flushTimer = new Timer(OnFlushTimer, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        _initialized = true;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken);
        if (_flushTimer != null)
            await _flushTimer.DisposeAsync();
        _flushLock.Dispose();
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
            
            var json = JsonSerializer.Serialize(_persistent, jsonOptions);

            await File.WriteAllTextAsync(statsPath, json, cancellationToken);
            logger.LogDebug("statistics have been written to a file. path='{Path}'", statsPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "failed to flush stats");
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
        var appDataPath = pathProvider.GetAppDataPath();
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
}
