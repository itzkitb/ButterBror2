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
        ILogger<DashboardServer> logger)
    {
        _opts = opts.Value;
        _bridge = bridge;
        _metrics = metrics;
        _executor = executor;
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
