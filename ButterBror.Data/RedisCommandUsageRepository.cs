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

    public async Task<DateTime?> GetLastUsedAsync(string commandId)
    {
        IDatabase db = _redis.GetDatabase();
        string key = $"{CommandUsagePrefix}{commandId}";
        
        return await _redisPipeline.ExecuteAsync(async (state, ct) =>
        {
            var (database, cmdId) = state;
            RedisValue value = await database.StringGetAsync($"{CommandUsagePrefix}{cmdId}");
            
            if (value.IsNullOrEmpty)
            {
                return (DateTime?)null;
            }

            try
            {
                return DateTime.Parse(value.ToString());
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "Failed to parse command usage timestamp for command ID: {CommandId}", cmdId);
                return (DateTime?)null;
            }
        }, (db, commandId), CancellationToken.None);
    }

    public async Task SetLastUsedAsync(string commandId, DateTime timestamp)
    {
        IDatabase db = _redis.GetDatabase();
        string value = timestamp.ToString("O"); // ISO 8601 format for consistent datetime serialization
        
        await _redisPipeline.ExecuteAsync(async (state, ct) =>
        {
            var (database, cmdId, ts) = state;
            await database.StringSetAsync($"{CommandUsagePrefix}{cmdId}", ts);
        }, (db, commandId, value), CancellationToken.None);
    }
}