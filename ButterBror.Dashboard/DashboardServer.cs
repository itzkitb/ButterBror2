using System.Net;
using System.Text;
using System.Text.Json;
using ButterBror.Core.Interfaces;
using ButterBror.Dashboard.Models;
using ButterBror.Dashboard.Services;
using ButterBror.Dashboard.Sse;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ButterBror.Dashboard;

/// <summary>
/// Dashboard server that serves static HTML pages and SSE streams using HttpListener
/// </summary>
public class DashboardServer : IHostedService, IDisposable
{
    private readonly DashboardOptions _opts;
    private readonly IDashboardBridge _bridge;
    private readonly MetricsCollector _metrics;
    private readonly AdminCommandExecutor _executor;
    private readonly RedisExplorerService _redisExplorer;
    private readonly FileManagerService _fileManager;
    private readonly SseHub _hub;
    private readonly ILogger<DashboardServer> _logger;

    private HttpListener _listener = null!;
    private CancellationTokenSource _cts = null!;
    private Task _listenerTask = Task.CompletedTask;
    private Task _metricsTask = Task.CompletedTask;

    public DashboardServer(
        IOptions<DashboardOptions> opts,
        IDashboardBridge bridge,
        MetricsCollector metrics,
        AdminCommandExecutor executor,
        RedisExplorerService redisExplorer,
        FileManagerService fileManager,
        ILogger<DashboardServer> logger)
    {
        _opts = opts.Value;
        _bridge = bridge;
        _metrics = metrics;
        _executor = executor;
        _redisExplorer = redisExplorer;
        _fileManager = fileManager;
        _hub = new SseHub();
        _logger = logger;

        if (bridge is DashboardBridge impl)
        {
            impl.OnLogEntry += async entry =>
            {
                try { await _hub.BroadcastLogAsync(entry); }
                catch { /* */ }
            };
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_opts.Port}/");
        _listener.Start();
        _logger.LogInformation("Dashboard running on http://localhost:{Port}", _opts.Port);

        _listenerTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
        _metricsTask  = Task.Run(() => MetricsLoopAsync(_cts.Token), _cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _listener.Stop();
        await Task.WhenAll(_listenerTask, _metricsTask).ConfigureAwait(false);
        _logger.LogInformation("Dashboard stopped");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ctx = await _listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleAsync(ctx, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard accept error");
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req  = ctx.Request;
        var res  = ctx.Response;
        var path = req.Url?.AbsolutePath.TrimEnd('/') ?? "/";

        // Token auth (skip if not configured)
        if (!string.IsNullOrEmpty(_opts.AccessToken))
        {
            var token = req.QueryString["token"]
                     ?? req.Headers["X-Dashboard-Token"];
            if (token != _opts.AccessToken)
            {
                res.StatusCode = 401;
                res.Close();
                return;
            }
        }

        // CORS
        res.Headers.Add("Access-Control-Allow-Origin", "*");

        try
        {
            switch (path)
            {
                case "" or "/":
                    await ServePageAsync(res, "index.html", ct);
                    break;
                case "/logs":
                    await ServePageAsync(res, "logs.html", ct);
                    break;
                case "/commands":
                    await ServePageAsync(res, "commands.html", ct);
                    break;
                case "/redis":
                    await ServePageAsync(res, "redis.html", ct);
                    break;

                case "/api/sse/metrics":
                    await HandleSseAsync(res, "metrics", ct);
                    break;
                case "/api/sse/logs":
                    await HandleSseAsync(res, "logs", ct);
                    break;

                case "/api/metrics/snapshot" when req.HttpMethod == "GET":
                    await ServeJsonAsync(res, await _metrics.CollectAsync());
                    break;

                case "/api/logs/recent" when req.HttpMethod == "GET":
                    await ServeJsonAsync(res, _bridge.GetRecentLogs(200));
                    break;

                case "/api/commands/execute" when req.HttpMethod == "POST":
                    await HandleCommandExecuteAsync(req, res, ct);
                    break;

                // Redis Explorer API
                case "/api/redis/databases" when req.HttpMethod == "GET":
                    await HandleRedisDatabasesAsync(res, ct);
                    break;
                case "/api/redis/keys" when req.HttpMethod == "GET":
                    await HandleRedisScanKeysAsync(req, res, ct);
                    break;
                case "/api/redis/key" when req.HttpMethod == "GET":
                    await HandleRedisGetKeyAsync(req, res, ct);
                    break;
                case "/api/redis/key" when req.HttpMethod == "DELETE":
                    await HandleRedisDeleteKeyAsync(req, res, ct);
                    break;
                case "/api/redis/key/string" when req.HttpMethod == "POST":
                    await HandleRedisSetStringAsync(req, res, ct);
                    break;
                case "/api/redis/key/ttl" when req.HttpMethod == "POST":
                    await HandleRedisSetTtlAsync(req, res, ct);
                    break;
                case "/api/redis/key/persist" when req.HttpMethod == "POST":
                    await HandleRedisPersistKeyAsync(req, res, ct);
                    break;
                case "/api/redis/key/rename" when req.HttpMethod == "POST":
                    await HandleRedisRenameKeyAsync(req, res, ct);
                    break;

                // Hash operations
                case "/api/redis/hash/all" when req.HttpMethod == "GET":
                    await HandleRedisHashGetAllAsync(req, res, ct);
                    break;
                case "/api/redis/hash/set" when req.HttpMethod == "POST":
                    await HandleRedisHashSetAsync(req, res, ct);
                    break;
                case "/api/redis/hash/field" when req.HttpMethod == "DELETE":
                    await HandleRedisHashDeleteFieldAsync(req, res, ct);
                    break;

                // List operations
                case "/api/redis/list/all" when req.HttpMethod == "GET":
                    await HandleRedisListGetAllAsync(req, res, ct);
                    break;
                case "/api/redis/list/push" when req.HttpMethod == "POST":
                    await HandleRedisListPushAsync(req, res, ct);
                    break;
                case "/api/redis/list/item" when req.HttpMethod == "DELETE":
                    await HandleRedisListRemoveAsync(req, res, ct);
                    break;

                // Set operations
                case "/api/redis/set/all" when req.HttpMethod == "GET":
                    await HandleRedisSetGetAllAsync(req, res, ct);
                    break;
                case "/api/redis/set/add" when req.HttpMethod == "POST":
                    await HandleRedisSetAddAsync(req, res, ct);
                    break;
                case "/api/redis/set/member" when req.HttpMethod == "DELETE":
                    await HandleRedisSetRemoveMemberAsync(req, res, ct);
                    break;

                // ZSet operations
                case "/api/redis/zset/all" when req.HttpMethod == "GET":
                    await HandleRedisZSetGetAllAsync(req, res, ct);
                    break;
                case "/api/redis/zset/add" when req.HttpMethod == "POST":
                    await HandleRedisZSetAddAsync(req, res, ct);
                    break;
                case "/api/redis/zset/member" when req.HttpMethod == "DELETE":
                    await HandleRedisZSetRemoveMemberAsync(req, res, ct);
                    break;

                // Stream operations
                case "/api/redis/stream/read" when req.HttpMethod == "GET":
                    await HandleRedisStreamReadAsync(req, res, ct);
                    break;

                // File Manager API
                case "/files":
                    await ServePageAsync(res, "files.html", ct);
                    break;

                case "/api/files/list" when req.HttpMethod == "GET":
                    await HandleFilesListAsync(req, res, ct);
                    break;

                case "/api/files/upload" when req.HttpMethod == "POST":
                    await HandleFilesUploadAsync(req, res, ct);
                    break;

                case "/api/files/delete" when req.HttpMethod == "DELETE":
                    await HandleFilesDeleteAsync(req, res, ct);
                    break;

                case "/api/files/mkdir" when req.HttpMethod == "POST":
                    await HandleFilesMkdirAsync(req, res, ct);
                    break;

                case "/api/files/rename" when req.HttpMethod == "POST":
                    await HandleFilesRenameAsync(req, res, ct);
                    break;

                case string p when p.StartsWith("/api/files/download") && req.HttpMethod == "GET":
                    await HandleFilesDownloadAsync(req, res, ct);
                    break;

                default:
                    res.StatusCode = 404;
                    res.Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard handler error for {Path}", path);
            try { res.StatusCode = 500; res.Close(); } catch { }
        }
    }

    private async Task HandleSseAsync(
        HttpListenerResponse res,
        string type,
        CancellationToken ct)
    {
        res.ContentType = "text/event-stream";
        res.Headers.Add("Cache-Control", "no-cache");
        res.Headers.Add("X-Accel-Buffering", "no");

        var writer = new StreamWriter(res.OutputStream, Encoding.UTF8, leaveOpen: true);
        var conn = new SseConnection(type, writer);
        _hub.Add(conn);

        // Send initial data burst
        if (type == "logs")
        {
            var recent = _bridge.GetRecentLogs(200);
            foreach (var entry in recent)
            {
                var ok = await conn.SendAsync("log", JsonSerializer.Serialize(entry));
                if (!ok) break;
            }
        }
        else if (type == "metrics")
        {
            var snapshot = await _metrics.CollectAsync();
            await conn.SendAsync("metrics", JsonSerializer.Serialize(snapshot));
        }

        // Keep alive until disconnect
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(15_000, ct);
                var ok = await conn.SendAsync("ping", "{}");
                if (!ok) break;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _hub.Remove(conn.Id);
            conn.Dispose();
            await writer.DisposeAsync();
        }
    }

    private async Task MetricsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2_000, ct);
                var snapshot = await _metrics.CollectAsync();
                await _hub.BroadcastMetricsAsync(snapshot);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Metrics broadcast error");
            }
        }
    }

    private async Task HandleCommandExecuteAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        using var reader = new StreamReader(req.InputStream);
        var body = await reader.ReadToEndAsync(ct);
        var command = JsonSerializer.Deserialize<AdminCommandRequest>(body);

        if (command == null || string.IsNullOrWhiteSpace(command.CommandLine))
        {
            res.StatusCode = 400;
            await ServeJsonAsync(res, new { error = "Empty command" });
            return;
        }

        var result = await _executor.ExecuteAsync(command.CommandLine, ct);
        await ServeJsonAsync(res, result);
    }

    // Redis Explorer API Handlers
    private async Task HandleRedisDatabasesAsync(HttpListenerResponse res, CancellationToken ct)
    {
        try
        {
            var databases = await _redisExplorer.GetDatabasesInfoAsync();
            await ServeJsonAsync(res, databases);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Redis databases");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisScanKeysAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var pattern = req.QueryString["pattern"] ?? "*";
            var cursor = long.Parse(req.QueryString["cursor"] ?? "0");
            var count = int.Parse(req.QueryString["count"] ?? "200");
            var db = int.Parse(req.QueryString["db"] ?? "0");

            var result = await _redisExplorer.ScanKeysAsync(pattern, cursor, count, db);
            await ServeJsonAsync(res, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan Redis keys");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisGetKeyAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var key = req.QueryString["key"];
            var db = int.Parse(req.QueryString["db"] ?? "0");

            if (string.IsNullOrEmpty(key))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Key parameter is required" });
                return;
            }

            var detail = await _redisExplorer.GetKeyDetailAsync(key, db);
            if (detail == null)
            {
                res.StatusCode = 404;
                await ServeJsonAsync(res, new { error = "Key not found" });
                return;
            }

            await ServeJsonAsync(res, detail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Redis key details");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisDeleteKeyAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var key = req.QueryString["key"];
            var db = int.Parse(req.QueryString["db"] ?? "0");

            if (string.IsNullOrEmpty(key))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Key parameter is required" });
                return;
            }

            var result = await _redisExplorer.DeleteKeyAsync(key, db);
            await ServeJsonAsync(res, new { ok = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete Redis key");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisSetStringAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream);
            var body = await reader.ReadToEndAsync(ct);
            var request = JsonSerializer.Deserialize<RedisStringSetRequest>(body);

            if (request == null || string.IsNullOrEmpty(request.Key))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Invalid request" });
                return;
            }

            TimeSpan? ttl = request.TtlSeconds > 0 ? TimeSpan.FromSeconds(request.TtlSeconds) : null;
            await _redisExplorer.SetStringAsync(request.Key, request.Value, ttl, request.Db);
            await ServeJsonAsync(res, new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set Redis string");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisSetTtlAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream);
            var body = await reader.ReadToEndAsync(ct);
            var request = JsonSerializer.Deserialize<RedisTtlSetRequest>(body);

            if (request == null || string.IsNullOrEmpty(request.Key) || request.TtlSeconds <= 0)
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Invalid request" });
                return;
            }

            var result = await _redisExplorer.SetTtlAsync(request.Key, TimeSpan.FromSeconds(request.TtlSeconds), request.Db);
            await ServeJsonAsync(res, new { ok = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set Redis key TTL");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisPersistKeyAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream);
            var body = await reader.ReadToEndAsync(ct);
            var request = JsonSerializer.Deserialize<RedisPersistRequest>(body);

            if (request == null || string.IsNullOrEmpty(request.Key))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Invalid request" });
                return;
            }

            var result = await _redisExplorer.PersistKeyAsync(request.Key, request.Db);
            await ServeJsonAsync(res, new { ok = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist Redis key");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisRenameKeyAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream);
            var body = await reader.ReadToEndAsync(ct);
            var request = JsonSerializer.Deserialize<RedisRenameRequest>(body);

            if (request == null || string.IsNullOrEmpty(request.OldKey) || string.IsNullOrEmpty(request.NewKey))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Invalid request" });
                return;
            }

            var result = await _redisExplorer.RenameKeyAsync(request.OldKey, request.NewKey, request.Db);
            await ServeJsonAsync(res, new { ok = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename Redis key");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisHashGetAllAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var key = req.QueryString["key"];
            var db = int.Parse(req.QueryString["db"] ?? "0");

            if (string.IsNullOrEmpty(key))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Key parameter is required" });
                return;
            }

            var result = await _redisExplorer.HashGetAllAsync(key, db);
            await ServeJsonAsync(res, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Redis hash fields");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisHashSetAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream);
            var body = await reader.ReadToEndAsync(ct);
            var request = JsonSerializer.Deserialize<RedisHashFieldSetRequest>(body);

            if (request == null || string.IsNullOrEmpty(request.Key) || string.IsNullOrEmpty(request.Field))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Invalid request" });
                return;
            }

            await _redisExplorer.HSetAsync(request.Key, request.Field, request.Value, request.Db);
            await ServeJsonAsync(res, new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set Redis hash field");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisHashDeleteFieldAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var key = req.QueryString["key"];
            var field = req.QueryString["field"];
            var db = int.Parse(req.QueryString["db"] ?? "0");

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(field))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Key and field parameters are required" });
                return;
            }

            var result = await _redisExplorer.HDelAsync(key, field, db);
            await ServeJsonAsync(res, new { ok = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete Redis hash field");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisListGetAllAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var key = req.QueryString["key"];
            var db = int.Parse(req.QueryString["db"] ?? "0");

            if (string.IsNullOrEmpty(key))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Key parameter is required" });
                return;
            }

            var result = await _redisExplorer.ListGetAllAsync(key, db);
            await ServeJsonAsync(res, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Redis list items");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisListPushAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream);
            var body = await reader.ReadToEndAsync(ct);
            var request = JsonSerializer.Deserialize<RedisListPushRequest>(body);

            if (request == null || string.IsNullOrEmpty(request.Key))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Invalid request" });
                return;
            }

            await _redisExplorer.ListPushAsync(request.Key, request.Value, request.Tail, request.Db);
            await ServeJsonAsync(res, new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push to Redis list");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisListRemoveAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var key = req.QueryString["key"];
            var value = req.QueryString["value"];
            var db = int.Parse(req.QueryString["db"] ?? "0");

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Key and value parameters are required" });
                return;
            }

            var result = await _redisExplorer.ListRemoveAsync(key, value, db);
            await ServeJsonAsync(res, new { removed = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove from Redis list");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisSetGetAllAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var key = req.QueryString["key"];
            var db = int.Parse(req.QueryString["db"] ?? "0");

            if (string.IsNullOrEmpty(key))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Key parameter is required" });
                return;
            }

            var result = await _redisExplorer.SetGetAllAsync(key, db);
            await ServeJsonAsync(res, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Redis set members");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisSetAddAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream);
            var body = await reader.ReadToEndAsync(ct);
            var request = JsonSerializer.Deserialize<RedisSetAddRequest>(body);

            if (request == null || string.IsNullOrEmpty(request.Key))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Invalid request" });
                return;
            }

            await _redisExplorer.SetAddAsync(request.Key, request.Value, request.Db);
            await ServeJsonAsync(res, new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add to Redis set");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisSetRemoveMemberAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var key = req.QueryString["key"];
            var value = req.QueryString["value"];
            var db = int.Parse(req.QueryString["db"] ?? "0");

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Key and value parameters are required" });
                return;
            }

            var result = await _redisExplorer.SetRemoveAsync(key, value, db);
            await ServeJsonAsync(res, new { ok = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove from Redis set");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisZSetGetAllAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var key = req.QueryString["key"];
            var db = int.Parse(req.QueryString["db"] ?? "0");

            if (string.IsNullOrEmpty(key))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Key parameter is required" });
                return;
            }

            var result = await _redisExplorer.ZSetGetAllAsync(key, db);
            await ServeJsonAsync(res, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Redis sorted set members");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisZSetAddAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream);
            var body = await reader.ReadToEndAsync(ct);
            var request = JsonSerializer.Deserialize<RedisZSetAddRequest>(body);

            if (request == null || string.IsNullOrEmpty(request.Key) || string.IsNullOrEmpty(request.Member))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Invalid request" });
                return;
            }

            await _redisExplorer.ZSetAddAsync(request.Key, request.Member, request.Score, request.Db);
            await ServeJsonAsync(res, new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add to Redis sorted set");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisZSetRemoveMemberAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var key = req.QueryString["key"];
            var member = req.QueryString["member"];
            var db = int.Parse(req.QueryString["db"] ?? "0");

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(member))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Key and member parameters are required" });
                return;
            }

            var result = await _redisExplorer.ZSetRemoveAsync(key, member, db);
            await ServeJsonAsync(res, new { ok = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove from Redis sorted set");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleRedisStreamReadAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var key = req.QueryString["key"];
            var count = int.Parse(req.QueryString["count"] ?? "100");
            var db = int.Parse(req.QueryString["db"] ?? "0");

            if (string.IsNullOrEmpty(key))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Key parameter is required" });
                return;
            }

            var result = await _redisExplorer.StreamReadAsync(key, count, db);
            await ServeJsonAsync(res, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read Redis stream");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    // File Manager API Handlers
    private async Task HandleFilesListAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var relativePath = req.QueryString["path"] ?? string.Empty;
            var entries = await _fileManager.ListDirectoryAsync(relativePath);
            await ServeJsonAsync(res, entries);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "File manager access denied");
            res.StatusCode = 403;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
        catch (DirectoryNotFoundException ex)
        {
            res.StatusCode = 404;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list directory");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleFilesUploadAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var dir = req.QueryString["dir"] ?? string.Empty;
            var name = req.QueryString["name"];

            if (string.IsNullOrWhiteSpace(name))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "File name is required" });
                return;
            }

            await _fileManager.UploadFileAsync(dir, name, req.InputStream);
            await ServeJsonAsync(res, new { ok = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "File manager access denied");
            res.StatusCode = 403;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("size exceeds"))
        {
            res.StatusCode = 413;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleFilesDeleteAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream);
            var body = await reader.ReadToEndAsync(ct);
            var request = JsonSerializer.Deserialize<DeleteRequest>(body);

            if (request == null || string.IsNullOrEmpty(request.Path))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Path is required" });
                return;
            }

            await _fileManager.DeleteAsync(request.Path);
            await ServeJsonAsync(res, new { ok = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "File manager access denied");
            res.StatusCode = 403;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            res.StatusCode = 404;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleFilesMkdirAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream);
            var body = await reader.ReadToEndAsync(ct);
            var request = JsonSerializer.Deserialize<CreateDirectoryRequest>(body);

            if (request == null || string.IsNullOrEmpty(request.Path))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Path is required" });
                return;
            }

            await _fileManager.CreateDirectoryAsync(request.Path);
            await ServeJsonAsync(res, new { ok = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "File manager access denied");
            res.StatusCode = 403;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create directory");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleFilesRenameAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream);
            var body = await reader.ReadToEndAsync(ct);
            var request = JsonSerializer.Deserialize<RenameRequest>(body);

            if (request == null || string.IsNullOrEmpty(request.Path) || string.IsNullOrEmpty(request.NewName))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Path and newName are required" });
                return;
            }

            await _fileManager.RenameAsync(request.Path, request.NewName);
            await ServeJsonAsync(res, new { ok = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "File manager access denied");
            res.StatusCode = 403;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            res.StatusCode = 404;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename file");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private async Task HandleFilesDownloadAsync(
        HttpListenerRequest req,
        HttpListenerResponse res,
        CancellationToken ct)
    {
        try
        {
            var relativePath = req.QueryString["path"];

            if (string.IsNullOrEmpty(relativePath))
            {
                res.StatusCode = 400;
                await ServeJsonAsync(res, new { error = "Path parameter is required" });
                return;
            }

            using var stream = await _fileManager.GetFileStreamAsync(relativePath);
            var fileName = Path.GetFileName(relativePath);
            
            res.ContentType = "application/octet-stream";
            res.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
            res.ContentLength64 = stream.Length;
            await stream.CopyToAsync(res.OutputStream, ct);
            res.Close();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "File manager access denied");
            res.StatusCode = 403;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            res.StatusCode = 404;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file");
            res.StatusCode = 500;
            await ServeJsonAsync(res, new { error = ex.Message });
        }
    }

    private static async Task ServePageAsync(
        HttpListenerResponse res,
        string resourceName,
        CancellationToken ct)
    {
        var asm = typeof(DashboardServer).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));

        if (name == null)
        {
            res.StatusCode = 404;
            res.Close();
            return;
        }

        using var stream = asm.GetManifestResourceStream(name)!;
        res.ContentType = "text/html; charset=utf-8";
        res.ContentLength64 = stream.Length;
        await stream.CopyToAsync(res.OutputStream, ct);
        res.Close();
    }

    private static async Task ServeJsonAsync(HttpListenerResponse res, object data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _listener?.Close();
    }
}
