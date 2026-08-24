using ButterBror.Domain.Entities;

namespace ButterBror.Data.Interfaces;

/// <summary>
/// Repository for storing user data
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Get a user by unified ID
    /// </summary>
    /// <param name="unifiedId">User unified ID</param>
    /// <returns>User profile</returns>
    Task<UserProfile?> GetByUnifiedIdAsync(Guid unifiedId);
    
    /// <summary>
    /// Get a user by platform ID
    /// </summary>
    /// <param name="platform">Platform name</param>
    /// <param name="platformId">User profile ID</param>
    /// <returns>User profile</returns>
    Task<UserProfile?> GetByPlatformIdAsync(string platform, string platformId);
    
    /// <summary>
    /// Get user by name
    /// </summary>
    /// <param name="displayName">Display name</param>
    /// <returns>User profile</returns>
    Task<UserProfile?> GetByDisplayNameAsync(string displayName);
    
    /// <summary>
    /// Create or update a user
    /// </summary>
    /// <param name="user">User info</param>
    /// <returns>User profile</returns>
    Task<UserProfile> CreateOrUpdateAsync(UserProfile user);
    
    /// <summary>
    /// Check if a user exists
    /// </summary>
    /// <param name="unifiedId">User unified ID</param>
    /// <returns>Does the user exist?</returns>
    Task<bool> UserExistsAsync(Guid unifiedId);
    
    /// <summary>
    /// Find a user
    /// </summary>
    /// <param name="platform">Platform name</param>
    /// <param name="identifier">Display name or ID</param>
    /// <returns>User profile</returns>
    Task<UserProfile?> FindUserAsync(string platform, string identifier);
}
