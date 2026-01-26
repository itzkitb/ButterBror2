using ButterBror.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace ButterBror.Infrastructure.Data;

public interface IUserRepository
{
    Task<UserProfile?> GetByUnifiedIdAsync(Guid unifiedId);
    Task<UserProfile?> GetByPlatformIdAsync(string platform, string platformId);
    Task<UserProfile> CreateOrUpdateAsync(UserProfile user);
    Task<bool> UserExistsAsync(Guid unifiedId);
    Task<List<UserProfile>> GetAllUsersAsync();
}
