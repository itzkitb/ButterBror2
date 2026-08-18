using ButterBror.Core.Interfaces;
using ButterBror.Domain.Entities;
using ButterBror.Data;
using Microsoft.Extensions.Logging;
using ButterBror.Domain;

namespace ButterBror.Infrastructure.Services;

public class UserService(
    IUserRepository userRepository,
    ICommandUsageRepository commandUsageRepository,
    ILogger<UserService> logger)
    : IUserService
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
    }

    public async Task<UserProfile> GetOrCreateUserAsync(string platformId, string platform, string displayName)
    {
        var user = await userRepository.GetByPlatformIdAsync(platform, platformId);

        if (user != null)
        {
            user.LastActive = DateTime.UtcNow;
            user.DisplayName = displayName;
            await userRepository.CreateOrUpdateAsync(user);
            return user;
        }

        var newUser = new UserProfile
        {
            UnifiedId = Guid.NewGuid(),
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
            LastActive = DateTime.UtcNow
        };

        newUser.AddPlatformId(platform, platformId);

        return await userRepository.CreateOrUpdateAsync(newUser);
    }
    
    public async Task UpdateUserStatisticsAsync(Guid unifiedUserId, string commandId, bool success)
    {
        var user = await userRepository.GetByUnifiedIdAsync(unifiedUserId);

        if (user == null)
        {
            logger.LogWarning("User with ID {UnifiedUserId} not found for statistics update", unifiedUserId);
            return;
        }

        // Updating team statistics
        var commandKey = commandId.ToLower();
        user.Statistics.TryAdd(commandKey, 0);
        user.Statistics[commandKey] = (int)user.Statistics[commandKey] + 1;

        // Updating general statistics
        var totalCommandsKey = "commands.total";
        user.Statistics.TryAdd(totalCommandsKey, 0);

        user.Statistics[totalCommandsKey] = (int)user.Statistics[totalCommandsKey] + 1;

        if (success)
        {
            var successfulCommandsKey = "commands.successful";
            user.Statistics.TryAdd(successfulCommandsKey, 0);
            user.Statistics[successfulCommandsKey] = (int)user.Statistics[successfulCommandsKey] + 1;
        }

        await userRepository.CreateOrUpdateAsync(user);
    }

    public async Task<DateTime?> GetCommandLastUsedAsync(string commandId, Guid userId)
    {
        return await commandUsageRepository.GetLastUsedAsync(commandId, userId);
    }
    
    public async Task SetCommandLastUseAsync(string commandId, Guid userId, DateTime date)
    {
        await commandUsageRepository.SetLastUsedAsync(commandId, userId, date);
    }
}
