using ButterBror.Domain.Entities;

namespace ButterBror.Data;

public interface IUserRepository
{
    Task<UserProfile?> GetByUnifiedIdAsync(Guid unifiedId);
    Task<UserProfile?> GetByPlatformIdAsync(string platform, string platformId);
    Task<UserProfile?> GetByDisplayNameAsync(string displayName);
    Task<UserProfile> CreateOrUpdateAsync(UserProfile user);
    Task<bool> UserExistsAsync(Guid unifiedId);
    Task<UserProfile?> FindUserAsync(string platform, string identifier);
}
