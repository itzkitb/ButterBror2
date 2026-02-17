using ButterBror.Domain.Entities;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Permissions manager for checking, adding and removing user rights
/// </summary>
public interface IPermissionManager
{
    Task<bool> HasPermissionAsync(Guid unifiedUserId, string requiredPermission);
    Task<bool> AddPermissionAsync(Guid unifiedUserId, string permission);
    Task<bool> RemovePermissionAsync(Guid unifiedUserId, string permission);
    Task<IReadOnlyList<string>> GetPermissionsAsync(Guid unifiedUserId);
}
