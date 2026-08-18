using ButterBror.Core.Interfaces;
using StackExchange.Redis;

namespace ButterBror.Infrastructure.Services;

public class RestrictionService(IConnectionMultiplexer redis) : IRestrictionService
{
    private readonly IDatabase _redis = redis.GetDatabase();

    // ><> Users
    public async Task<UserBlockStatus> CheckUserBlockStatusAsync(string platform, Guid userId, CancellationToken ct = default)
    {
        var plat = platform.ToLowerInvariant();
        var globalKey = $"blocks:users:global:{userId}";
        var platformKey = $"blocks:users:local:{plat}:{userId}";

        var isGlobalBlocked = await _redis.KeyExistsAsync(globalKey);
        var isPlatformBlocked = await _redis.KeyExistsAsync(platformKey);

        if (!isGlobalBlocked && !isPlatformBlocked)
        {
            return new UserBlockStatus(IsBlocked: false, ShouldNotify: false);
        }
        
        var notifiedKey = $"blocks:users:notified:{plat}:{userId}";
        bool isFirstNotification = await _redis.StringSetAsync(notifiedKey, "1", when: When.NotExists);

        return new UserBlockStatus(IsBlocked: true, ShouldNotify: isFirstNotification);
    }

    public async Task<bool> BlockUserAsync(string platform, Guid userId, string? reason = null, bool isGlobal = false, CancellationToken ct = default)
    {
        var key = isGlobal
            ? $"blocks:users:global:{userId}"
            : $"blocks:users:local:{platform.ToLowerInvariant()}:{userId}";

        return await _redis.StringSetAsync(key, reason ?? "No reason specified");
    }

    public async Task<bool> UnblockUserAsync(string platform, Guid userId, bool isGlobal = false, CancellationToken ct = default)
    {
        var plat = platform.ToLowerInvariant();

        var key = isGlobal
            ? $"blocks:users:global:{userId}"
            : $"blocks:users:local:{plat}:{userId}";
        
        var notifiedKey = $"blocks:users:notified:{plat}:{userId}";
        await _redis.KeyDeleteAsync(notifiedKey);
        
        return await _redis.KeyDeleteAsync(key);
    }

    // ><> Command Checks
    public async Task<CommandBlockStatus> CheckCommandStatusAsync(
        string platform, 
        string chatId, 
        string commandId, 
        CancellationToken ct = default)
    {
        var id = commandId.ToLowerInvariant();
        var plat = platform.ToLowerInvariant();

        // S0. Global
        if (await _redis.SetContainsAsync("blocks:cmd:global", id))
            return CommandBlockStatus.BlockedGlobally;

        // S1. Platform
        if (await _redis.SetContainsAsync($"blocks:cmd:platform:{plat}", id))
            return CommandBlockStatus.BlockedOnPlatform;

        // S2. Channel
        if (await _redis.SetContainsAsync($"blocks:cmd:chat:{plat}:{chatId}", id))
            return CommandBlockStatus.BlockedInChat;

        return CommandBlockStatus.Allowed;
    }

    // ><> Management
    public Task<bool> BlockCommandGlobalAsync(string commandId, CancellationToken ct = default) =>
        _redis.SetAddAsync("blocks:cmd:global", commandId.ToLowerInvariant());

    public Task<bool> UnblockCommandGlobalAsync(string commandId, CancellationToken ct = default) =>
        _redis.SetRemoveAsync("blocks:cmd:global", commandId.ToLowerInvariant());

    public Task<bool> BlockCommandPlatformAsync(string platform, string commandId, CancellationToken ct = default) =>
        _redis.SetAddAsync($"blocks:cmd:platform:{platform.ToLowerInvariant()}", commandId.ToLowerInvariant());

    public Task<bool> UnblockCommandPlatformAsync(string platform, string commandId, CancellationToken ct = default) =>
        _redis.SetRemoveAsync($"blocks:cmd:platform:{platform.ToLowerInvariant()}", commandId.ToLowerInvariant());

    public Task<bool> BlockCommandChatAsync(string platform, string chatId, string commandId, CancellationToken ct = default) =>
        _redis.SetAddAsync($"blocks:cmd:chat:{platform.ToLowerInvariant()}:{chatId}", commandId.ToLowerInvariant());

    public Task<bool> UnblockCommandChatAsync(string platform, string chatId, string commandId, CancellationToken ct = default) =>
        _redis.SetRemoveAsync($"blocks:cmd:chat:{platform.ToLowerInvariant()}:{chatId}", commandId.ToLowerInvariant());
}