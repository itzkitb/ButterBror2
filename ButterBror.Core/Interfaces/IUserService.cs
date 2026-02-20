using ButterBror.Domain.Entities;

namespace ButterBror.Core.Interfaces;

public interface IUserService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<UserProfile> GetOrCreateUserAsync(string platformId, string platform, string displayName);
    Task UpdateUserStatisticsAsync(Guid unifiedUserId, string commandName, bool success);
    Task<DateTime?> GetCommandLastUsedAsync(string commandName, Guid userId);
    Task SetCommandLastUseAsync(string commandName, Guid userId, DateTime date);
}
