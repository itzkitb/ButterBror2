namespace ButterBror.Core.Interfaces;

public record UserBlockStatus(bool IsBlocked, bool ShouldNotify);

/// <summary>
/// Service for blocking users and commands
/// </summary>
public interface IRestrictionService
{
    /// <summary>
    /// Check user block status
    /// </summary>
    /// <param name="platform">Platform ID</param>
    /// <param name="userId">User ID</param>
    /// <param name="ct">Cancelation token</param>
    /// <returns>Blocking status</returns>
    Task<UserBlockStatus> CheckUserBlockStatusAsync(
        string platform,
        Guid userId,
        CancellationToken ct = default);
    
    /// <summary>
    /// Block a user
    /// </summary>
    /// <param name="platform">Platform ID</param>
    /// <param name="userId">User ID</param>
    /// <param name="reason">Block reason</param>
    /// <param name="isGlobal">Is global block (If not, the block will only occur on the specified platform)</param>
    /// <param name="ct">Cancelation token</param>
    /// <returns></returns>
    Task<bool> BlockUserAsync(
        string platform,
        Guid userId,
        string? reason = null,
        bool isGlobal = false,
        CancellationToken ct = default);
    
    /// <summary>
    /// Unblock a user
    /// </summary>
    /// <param name="platform">Platform ID</param>
    /// <param name="userId">User ID</param>
    /// <param name="isGlobal">Is global block (If not, the block will only occur on the specified platform)</param>
    /// <param name="ct">Cancelation token</param>
    /// <returns></returns>
    Task<bool> UnblockUserAsync(
        string platform,
        Guid userId,
        bool isGlobal = false,
        CancellationToken ct = default);
    
    /// <summary>
    /// Check the command block status
    /// </summary>
    /// <param name="platform">Platform ID</param>
    /// <param name="chatId">Chat ID</param>
    /// <param name="commandId">Command ID</param>
    /// <param name="ct">Cancelation token</param>
    /// <returns></returns>
    Task<CommandBlockStatus> CheckCommandStatusAsync(
        string platform,
        string chatId,
        string commandId,
        CancellationToken ct = default);
    
    /// <summary>
    /// Block a command globally
    /// </summary>
    /// <param name="commandId">Command ID</param>
    /// <param name="ct">Cancelation token</param>
    /// <returns></returns>
    Task<bool> BlockCommandGlobalAsync(
        string commandId,
        CancellationToken ct = default);
    
    /// <summary>
    /// Unblock a command globally
    /// </summary>
    /// <param name="commandId">Command ID</param>
    /// <param name="ct">Cancelation token</param>
    /// <returns></returns>
    Task<bool> UnblockCommandGlobalAsync(
        string commandId,
        CancellationToken ct = default);

    /// <summary>
    /// Block a command on the platform
    /// </summary>
    /// <param name="platform">Platform ID</param>
    /// <param name="commandId">Command ID</param>
    /// <param name="ct">Cancelation token</param>
    /// <returns></returns>
    Task<bool> BlockCommandPlatformAsync(
        string platform,
        string commandId,
        CancellationToken ct = default);
    
    /// <summary>
    /// Unblock a command on the platform
    /// </summary>
    /// <param name="platform">Platform ID</param>
    /// <param name="commandId">Command ID</param>
    /// <param name="ct">Cancelation token</param>
    /// <returns></returns>
    Task<bool> UnblockCommandPlatformAsync(
        string platform,
        string commandId,
        CancellationToken ct = default);

    /// <summary>
    /// Block a command in chat
    /// </summary>
    /// <param name="platform">Platform ID</param>
    /// <param name="chatId">Chat ID</param>
    /// <param name="commandId">Command ID</param>
    /// <param name="ct">Cancelation token</param>
    /// <returns></returns>
    Task<bool> BlockCommandChatAsync(
        string platform,
        string chatId,
        string commandId,
        CancellationToken ct = default);
    
    /// <summary>
    /// Unblock a command in chat
    /// </summary>
    /// <param name="platform">Platform ID</param>
    /// <param name="chatId"></param>
    /// <param name="commandId"></param>
    /// <param name="ct">Cancelation token</param>
    /// <returns></returns>
    Task<bool> UnblockCommandChatAsync(
        string platform,
        string chatId, 
        string commandId,
        CancellationToken ct = default);
}

public enum CommandBlockStatus
{
    Allowed,
    BlockedGlobally,
    BlockedOnPlatform,
    BlockedInChat
}