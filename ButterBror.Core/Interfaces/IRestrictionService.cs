namespace ButterBror.Core.Interfaces;

public record UserBlockStatus(bool IsBlocked, bool ShouldNotify);

public interface IRestrictionService
{
    Task<UserBlockStatus> CheckUserBlockStatusAsync(string platform, Guid userId, CancellationToken ct = default);
    Task<bool> BlockUserAsync(string platform, Guid userId, string? reason = null, bool isGlobal = false, CancellationToken ct = default);
    Task<bool> UnblockUserAsync(string platform, Guid userId, bool isGlobal = false, CancellationToken ct = default);
    
    Task<CommandBlockStatus> CheckCommandStatusAsync(string platform, string channelId, string commandId, CancellationToken ct = default);
    
    Task<bool> BlockCommandGlobalAsync(string commandId, CancellationToken ct = default);
    Task<bool> UnblockCommandGlobalAsync(string commandId, CancellationToken ct = default);

    Task<bool> BlockCommandPlatformAsync(string platform, string commandId, CancellationToken ct = default);
    Task<bool> UnblockCommandPlatformAsync(string platform, string commandId, CancellationToken ct = default);

    Task<bool> BlockCommandChatAsync(string platform, string channelId, string commandId, CancellationToken ct = default);
    Task<bool> UnblockCommandChatAsync(string platform, string channelId, string commandId, CancellationToken ct = default);
}

public enum CommandBlockStatus
{
    Allowed,
    BlockedGlobally,
    BlockedOnPlatform,
    BlockedInChat
}