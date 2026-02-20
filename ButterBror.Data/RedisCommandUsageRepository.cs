using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;
using StackExchange.Redis;
using System.Text.Json;
using ButterBror.Domain;

namespace ButterBror.Data;

public class RedisCommandUsageRepository : ICommandUsageRepository
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ResiliencePipeline _redisPipeline;
    private readonly ILogger<RedisCommandUsageRepository> _logger;
    private const string CommandUsagePrefix = "command_usage:";

    public RedisCommandUsageRepository(IConnectionMultiplexer redis, ILogger<RedisCommandUsageRepository> logger, ResiliencePipelineProvider<string> pipelineProvider)
    {
        _redis = redis;
        _redisPipeline = pipelineProvider.GetPipeline("redis");
        _logger = logger;
    }

    public async Task<DateTime?> GetLastUsedAsync(string commandId, Guid userId)
    {
        IDatabase db = _redis.GetDatabase();
        string key = $"{CommandUsagePrefix}{commandId}:{userId}";

        return await _redisPipeline.ExecuteAsync(async (state, ct) =>
        {
            var (database, cmdId, usrId) = state;
            RedisValue value = await database.StringGetAsync($"{CommandUsagePrefix}{cmdId}:{usrId}");

            if (value.IsNullOrEmpty)
            {
                return (DateTime?)null;
            }

            try
            {
                return DateTime.Parse(value.ToString()).ToUniversalTime();
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "Failed to parse command usage timestamp for command ID: {CommandId}, User ID: {UserId}", cmdId, usrId);
                return (DateTime?)null;
            }
        }, (db, commandId, userId), CancellationToken.None);
    }

    public async Task SetLastUsedAsync(string commandId, Guid userId, DateTime timestamp)
    {
        IDatabase db = _redis.GetDatabase();
        string value = timestamp.ToString("O"); // ISO 8601 format for consistent datetime serialization

        await _redisPipeline.ExecuteAsync(async (state, ct) =>
        {
            var (database, cmdId, usrId, ts) = state;
            await database.StringSetAsync($"{CommandUsagePrefix}{cmdId}:{usrId}", ts);
        }, (db, commandId, userId, value), CancellationToken.None);
    }
}