using System.Text.Json;
using ButterBror.Domain.Entities;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;
using StackExchange.Redis;

namespace ButterBror.Data;

/// <summary>
/// Redis implementation of error report repository
/// </summary>
public class ErrorReportRepository : IErrorReportRepository
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ResiliencePipeline _redisPipeline;
    private readonly ILogger<ErrorReportRepository> _logger;
    private const string ErrorPrefix = "error:";
    private const string UserIndexPrefix = "error:user:";

    public ErrorReportRepository(
        IConnectionMultiplexer redis,
        ILogger<ErrorReportRepository> logger,
        ResiliencePipelineProvider<string> pipelineProvider)
    {
        _redis = redis;
        _logger = logger;
        _redisPipeline = pipelineProvider.GetPipeline("redis");
    }

    public async Task SaveAsync(ErrorReport report)
    {
        await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            string key = $"{ErrorPrefix}{report.ErrorId}";
            string json = JsonSerializer.Serialize(report);
            await db.StringSetAsync(key, json);

            // Index by user ID if available
            if (report.UserId.HasValue)
            {
                string userIndexKey = $"{UserIndexPrefix}{report.UserId}";
                await db.ListLeftPushAsync(userIndexKey, report.ErrorId);
                // Keep only last 100 errors per user
                await db.ListTrimAsync(userIndexKey, 0, 99);
            }

            _logger.LogDebug("Saved error report: {ErrorId}", report.ErrorId);
        });
    }

    public async Task<ErrorReport?> GetByIdAsync(string errorId)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            string key = $"{ErrorPrefix}{errorId}";
            RedisValue json = await db.StringGetAsync(key);
            return json.HasValue ? JsonSerializer.Deserialize<ErrorReport>(json.ToString()) : null;
        });
    }

    public async Task<IReadOnlyList<ErrorReport>> GetByUserIdAsync(Guid userId)
    {
        return await _redisPipeline.ExecuteAsync(async ct =>
        {
            IDatabase db = _redis.GetDatabase();
            string userIndexKey = $"{UserIndexPrefix}{userId}";
            RedisValue[] errorIds = await db.ListRangeAsync(userIndexKey);
            var reports = new List<ErrorReport>();

            foreach (var errorId in errorIds)
            {
                var report = await GetByIdAsync(errorId.ToString()!);
                if (report != null)
                {
                    reports.Add(report);
                }
            }

            return reports.AsReadOnly();
        });
    }
}