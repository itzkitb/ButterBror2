using ButterBror.Core.Interfaces;
using ButterBror.Data;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class PermissionManager : IPermissionManager
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<PermissionManager> _logger;

    public PermissionManager(IUserRepository userRepository, ILogger<PermissionManager> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<bool> HasPermissionAsync(Guid unifiedUserId, string requiredPermission)
    {
        if (string.IsNullOrWhiteSpace(requiredPermission))
        {
            _logger.LogWarning("An attempt to verify an empty permission");
            return false;
        }

        var user = await _userRepository.GetByUnifiedIdAsync(unifiedUserId);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found", unifiedUserId);
            return false;
        }

        foreach (var userPermission in user.Permissions)
        {
            if (MatchesPermission(userPermission, requiredPermission))
            {
                _logger.LogDebug(
                    "The permission {RequiredPermission} was found through the {UserPermission} pattern",
                    requiredPermission,
                    userPermission
                );
                return true;
            }
        }

        _logger.LogDebug("Permission {RequiredPermission} not found", requiredPermission);
        return false;
    }

    private static bool MatchesPermission(string userPermission, string requiredPermission)
    {
        if (string.Equals(userPermission, requiredPermission, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (userPermission.Contains('*'))
        {
            return MatchWildcard(userPermission, requiredPermission);
        }

        return false;
    }

    private static bool MatchWildcard(string pattern, string value)
    {
        // S0: Normalizing the pattern
        pattern = pattern.ToLowerInvariant();
        value = value.ToLowerInvariant();

        // S1: If the pattern is "*", then everything is suitable
        if (pattern == "*")
        {
            return true;
        }

        // S2: If the pattern ends with "*", check the prefix
        if (pattern.EndsWith('*'))
        {
            var prefix = pattern[..^1];
            
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Prefix ends with ":" => value must be longer than the prefix
            if (prefix.EndsWith(':') && value.Length > prefix.Length)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<bool> AddPermissionAsync(Guid unifiedUserId, string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            _logger.LogWarning("Attempt to add an empty permission");
            return false;
        }

        var user = await _userRepository.GetByUnifiedIdAsync(unifiedUserId);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found", unifiedUserId);
            return false;
        }

        // Normalize
        permission = permission.Trim();

        // Looking if the permission already exists
        if (user.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogDebug("The user {UserId} already has the {Permission} permission",
                unifiedUserId, permission);
            return false;
        }

        user.Permissions.Add(permission);
        await _userRepository.CreateOrUpdateAsync(user);

        _logger.LogInformation(
            "Added permission {Permission} to user {UserId}",
            permission, unifiedUserId
        );

        return true;
    }

    public async Task<bool> RemovePermissionAsync(Guid unifiedUserId, string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            _logger.LogWarning("Attempt to delete an empty permission");
            return false;
        }

        var user = await _userRepository.GetByUnifiedIdAsync(unifiedUserId);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found", unifiedUserId);
            return false;
        }

        // Normalize
        permission = permission.Trim();

        // Looking for the permission
        var existingPermission = user.Permissions.FirstOrDefault(
            p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase)
        );

        if (existingPermission == null)
        {
            _logger.LogDebug("Permission {Permission} not found in user {UserId}",
                permission, unifiedUserId);
            return false;
        }

        user.Permissions.Remove(existingPermission);
        await _userRepository.CreateOrUpdateAsync(user);

        _logger.LogInformation(
            "Permission {Permission} removed from user {UserId}",
            permission, unifiedUserId
        );

        return true;
    }

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(Guid unifiedUserId)
    {
        var user = await _userRepository.GetByUnifiedIdAsync(unifiedUserId);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found", unifiedUserId);
            return Array.Empty<string>();
        }

        return user.Permissions.AsReadOnly();
    }
}
