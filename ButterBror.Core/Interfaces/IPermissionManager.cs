using ButterBror.Domain.Entities;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Permissions manager for checking, adding and removing user rights
/// </summary>
public interface IPermissionManager
{
    /// <summary>
    /// Does the user have such permission?
    /// </summary>
    /// <param name="unifiedUserId">User ID</param>
    /// <param name="requiredPermission">Required permission</param>
    /// <returns>Does the user have this permission?</returns>
    Task<bool> HasPermissionAsync(Guid unifiedUserId, string requiredPermission);
    
    /// <summary>
    /// Add permission to user
    /// </summary>
    /// <param name="unifiedUserId">User ID</param>
    /// <param name="permission"></param>
    /// <returns>Success/Failure</returns>
    Task<bool> AddPermissionAsync(Guid unifiedUserId, string permission);
    
    /// <summary>
    /// Remove permission from a user
    /// </summary>
    /// <param name="unifiedUserId">User ID</param>
    /// <param name="permission"></param>
    /// <returns>Success/Failure</returns>
    Task<bool> RemovePermissionAsync(Guid unifiedUserId, string permission);
    
    /// <summary>
    /// Get a list of user permissions
    /// </summary>
    /// <param name="unifiedUserId">User ID</param>
    /// <returns>List of user permissions</returns>
    Task<IReadOnlyList<string>> GetPermissionsAsync(Guid unifiedUserId);
}
