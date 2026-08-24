using System.Text.Json;
using System.Text.Json.Serialization;
using ButterBror.Data.Interfaces;
using ButterBror.Domain.Entities;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;
using StackExchange.Redis;

namespace ButterBror.Data.Repositories;

public class ErrorReportRepository(
    IConnectionMultiplexer redis,
    ILogger<ErrorReportRepository> logger,
    ResiliencePipelineProvider<string> pipelineProvider)
    : IErrorReportRepository
{
    private readonly ResiliencePipeline _redisPipeline = pipelineProvider.GetPipeline("redis");
    private const string ErrorPrefix = "error:";
    private const string UserIndexPrefix = "error:user:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new SafeObjectConverter() },
        ReferenceHandler = ReferenceHandler.IgnoreCycles 
    };

    public async Task SaveAsync(ErrorReport report)
    {
        await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var key = $"{ErrorPrefix}{report.ErrorId}";
            var json = JsonSerializer.Serialize(report, JsonOptions);
            await db.StringSetAsync(key, json);
            
            if (report.UserId.HasValue)
            {
                var userIndexKey = $"{UserIndexPrefix}{report.UserId}";
                await db.ListLeftPushAsync(userIndexKey, report.ErrorId.ToString());
                await db.ListTrimAsync(userIndexKey, 0, 99);
            }

            logger.LogDebug("saved error report. id={ErrorId}", report.ErrorId);
        });
    }

    public async Task<ErrorReport?> GetByIdAsync(Guid errorId)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var key = $"{ErrorPrefix}{errorId}";
            var json = await db.StringGetAsync(key);
            
            return json.HasValue ? JsonSerializer.Deserialize<ErrorReport>(json.ToString(), JsonOptions) : null;
        });
    }

    public async Task<IReadOnlyList<ErrorReport>> GetByUserIdAsync(Guid userId)
    {
        return await _redisPipeline.ExecuteAsync(async _ =>
        {
            var db = redis.GetDatabase();
            var userIndexKey = $"{UserIndexPrefix}{userId}";
            var errorIds = await db.ListRangeAsync(userIndexKey);
            var reports = new List<ErrorReport>();

            foreach (var errorId in errorIds)
            {
                if (Guid.TryParse(errorId.ToString(), out var guid))
                    continue;
                
                var report = await GetByIdAsync(guid);
                if (report != null)
                {
                    reports.Add(report);
                }
            }

            return reports.AsReadOnly();
        });
    }
}