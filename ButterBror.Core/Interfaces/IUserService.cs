using ButterBror.Domain.Entities;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Service for working with users
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Initialize the service
    /// </summary>
    /// <param name="cancellationToken">Cancelation token</param>
    /// <returns></returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Create a user
    /// </summary>
    /// <param name="platformId">Platform ID</param>
    /// <param name="platform">Platform name</param>
    /// <param name="displayName">User DisplayName</param>
    /// <returns>User</returns>
    Task<UserProfile> GetOrCreateUserAsync(string platformId, string platform, string displayName);
    
    /// <summary>
    /// Update user statistics
    /// </summary>
    /// <param name="unifiedUserId">User ID</param>
    /// <param name="commandId">Command ID</param>
    /// <param name="success">Was the command executed successfully?</param>
    /// <returns></returns>
    Task UpdateUserStatisticsAsync(Guid unifiedUserId, string commandId, bool success);
    
    /// <summary>
    /// Find out the last use of a command
    /// </summary>
    /// <param name="commandId">Command ID</param>
    /// <param name="userId">User ID</param>
    /// <returns>DateTime of last command use</returns>
    Task<DateTime?> GetCommandLastUsedAsync(string commandId, Guid userId);
    
    /// <summary>
    /// Set the date the command was last used
    /// </summary>
    /// <param name="commandId">Command ID</param>
    /// <param name="userId">User ID</param>
    /// <param name="date">Date</param>
    /// <returns></returns>
    Task SetCommandLastUseAsync(string commandId, Guid userId, DateTime date);
}
