using ButterBror.Core.Interfaces;
using ButterBror.Data;
using ButterBror.Data.Interfaces;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class PermissionManager(IUserRepository userRepository, ILogger<PermissionManager> logger)
    : IPermissionManager
{
    public async Task<bool> HasPermissionAsync(Guid unifiedUserId, string requiredPermission)
    {
        if (string.IsNullOrWhiteSpace(requiredPermission))
        {
            logger.LogWarning("an attempt to verify an empty permission");
            return false;
        }

        var user = await userRepository.GetByUnifiedIdAsync(unifiedUserId);
        if (user == null)
        {
            logger.LogWarning("user not found, cant check permission. uid={UserId}", unifiedUserId);
            return false;
        }

        logger.LogDebug("users permission: {Permission}. uid={User}", string.Join(", ", user.Permissions), unifiedUserId);
        foreach (var userPermission in user.Permissions.Where(userPermission => MatchesPermission(userPermission, requiredPermission)))
        {
            logger.LogDebug(
                "the permission {RequiredPermission} was found through the {UserPermission} pattern",
                requiredPermission,
                userPermission
            );
            return true;
        }

        logger.LogDebug("permission {RequiredPermission} not found", requiredPermission);
        return false;
    }

    private static bool MatchesPermission(string userPermission, string requiredPermission)
    {
        if (string.Equals(userPermission, requiredPermission, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return userPermission.Contains('*') && MatchWildcard(userPermission, requiredPermission);
    }

    private static bool MatchWildcard(string pattern, string value)
    {
        // s0: normalizing the pattern
        pattern = pattern.ToLowerInvariant();
        value = value.ToLowerInvariant();

        // s1: if the pattern is "*", then everything is suitable
        if (pattern == "*")
        {
            return true;
        }

        // s2: if the pattern ends with "*", check the prefix
        if (!pattern.EndsWith('*'))
            return false;
        
        var prefix = pattern[..^1];
            
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // s3: prefix ends with ":" => value must be longer than the prefix
        return prefix.EndsWith(':') && value.Length > prefix.Length;
    }

    public async Task<bool> AddPermissionAsync(Guid unifiedUserId, string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            logger.LogWarning("attempt to add an empty permission. uid={Uid}, permission='{Permission}'", unifiedUserId, permission);
            return false;
        }

        var user = await userRepository.GetByUnifiedIdAsync(unifiedUserId);
        if (user == null)
        {
            logger.LogWarning("user not found, cant add permission. uid={UserId}", unifiedUserId);
            return false;
        }

        // s0: normalize
        permission = permission.Trim();

        // s1: looking if the permission already exists
        if (user.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase))
        {
            logger.LogDebug("the user already has the permission. uid={UserId}, permission={Permission}",
                unifiedUserId, permission);
            return false;
        }

        user.Permissions.Add(permission);
        await userRepository.CreateOrUpdateAsync(user);

        logger.LogInformation(
            "added permission. permission={Permission}, uid={UserId}",
            permission, unifiedUserId
        );

        return true;
    }

    public async Task<bool> RemovePermissionAsync(Guid unifiedUserId, string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            logger.LogWarning("attempt to delete an empty permission");
            return false;
        }

        var user = await userRepository.GetByUnifiedIdAsync(unifiedUserId);
        if (user == null)
        {
            logger.LogWarning(
                "user not found, cant remove permission. uid={UserId}, permission={Permission}",
                unifiedUserId,
                permission);
            return false;
        }

        // s0: normalize
        permission = permission.Trim();

        // s1: looking for the permission
        var existingPermission = user.Permissions.FirstOrDefault(
            p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase)
        );

        if (existingPermission == null)
        {
            logger.LogDebug("permission not found at user. uid={UserId}, permission={Permission}",
                unifiedUserId, permission);
            return false;
        }

        user.Permissions.Remove(existingPermission);
        await userRepository.CreateOrUpdateAsync(user);

        logger.LogInformation(
            "permission removed from user. uid={UserId}, permission={Permission}",
            permission, unifiedUserId
        );

        return true;
    }

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(Guid unifiedUserId)
    {
        var user = await userRepository.GetByUnifiedIdAsync(unifiedUserId);
        if (user != null)
            return user.Permissions.AsReadOnly();
        logger.LogWarning("user not found, cant get permissions. uid={UserId}", unifiedUserId);
        return [];
    }
}
