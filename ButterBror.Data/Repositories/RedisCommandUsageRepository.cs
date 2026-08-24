using ButterBror.Domain;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;
using StackExchange.Redis;

namespace ButterBror.Data.Repositories;

public class RedisCommandUsageRepository(
    IConnectionMultiplexer redis,
    ILogger<RedisCommandUsageRepository> logger,
    ResiliencePipelineProvider<string> pipelineProvider)
    : ICommandUsageRepository
{
    private readonly ResiliencePipeline _redisPipeline = pipelineProvider.GetPipeline("redis");
    private const string CommandUsagePrefix = "command_usage:";

    public async Task<DateTime?> GetLastUsedAsync(string commandId, Guid userId)
    {
        return await _redisPipeline.ExecuteAsync(async (state, _) =>
        {
            var db = redis.GetDatabase();
            var (cmdId, usrId) = state;
            var value = await db.StringGetAsync($"{CommandUsagePrefix}{cmdId}:{usrId}");

            if (value.IsNullOrEmpty)
            {
                return null;
            }

            try
            {
                return DateTime.Parse(value.ToString()).ToUniversalTime();
            }
            catch (FormatException ex)
            {
                logger.LogWarning(ex, 
                    "failed to parse command usage timestamp. cmdid={CommandId}, uid={UserId}",
                    cmdId,
                    usrId);
                return (DateTime?)null;
            }
        }, (commandId, userId), CancellationToken.None);
    }

    public async Task SetLastUsedAsync(string commandId, Guid userId, DateTime timestamp)
    {
        var db = redis.GetDatabase();
        var value = timestamp.ToString("O");

        await _redisPipeline.ExecuteAsync(async (state, _) =>
        {
            var (database, cmdId, usrId, ts) = state;
            await database.StringSetAsync($"{CommandUsagePrefix}{cmdId}:{usrId}", ts);
        }, (db, commandId, userId, value), CancellationToken.None);
    }
}