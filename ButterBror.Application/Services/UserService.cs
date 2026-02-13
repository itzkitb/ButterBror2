using ButterBror.Domain.Entities;
using ButterBror.Data;
using Microsoft.Extensions.Logging;
using ButterBror.Domain;

namespace ButterBror.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly ICommandUsageRepository _commandUsageRepository;
    private readonly ILogger<UserService> _logger;

    public UserService(IUserRepository userRepository, ICommandUsageRepository commandUsageRepository, ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _commandUsageRepository = commandUsageRepository;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
    }

    public async Task<UserProfile> GetOrCreateUserAsync(string platformId, string platform, string displayName)
    {
        var user = await _userRepository.GetByPlatformIdAsync(platform, platformId);

        if (user != null)
        {
            user.LastActive = DateTime.UtcNow;
            user.DisplayName = displayName;
            await _userRepository.CreateOrUpdateAsync(user);
            return user;
        }

        var newUser = new UserProfile
        {
            UnifiedUserId = Guid.NewGuid(),
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
            LastActive = DateTime.UtcNow
        };

        newUser.AddPlatformId(platform, platformId);

        return await _userRepository.CreateOrUpdateAsync(newUser);
    }

    public async Task UpdateUserStatisticsAsync(Guid unifiedUserId, string commandName, bool success)
    {
        var user = await _userRepository.GetByUnifiedIdAsync(unifiedUserId);

        if (user == null)
        {
            _logger.LogWarning("User with ID {UnifiedUserId} not found for statistics update", unifiedUserId);
            return;
        }

        // Updating team statistics
        var commandKey = $"commands.{commandName}".ToLower();
        if (!user.Statistics.ContainsKey(commandKey))
        {
            user.Statistics[commandKey] = 0;
        }

        user.Statistics[commandKey] = (int)user.Statistics[commandKey] + 1;

        // Updating general statistics
        var totalCommandsKey = "commands.total";
        if (!user.Statistics.ContainsKey(totalCommandsKey))
        {
            user.Statistics[totalCommandsKey] = 0;
        }

        user.Statistics[totalCommandsKey] = (int)user.Statistics[totalCommandsKey] + 1;

        if (success)
        {
            var successfulCommandsKey = "commands.successful";
            if (!user.Statistics.ContainsKey(successfulCommandsKey))
            {
                user.Statistics[successfulCommandsKey] = 0;
            }

            user.Statistics[successfulCommandsKey] = (int)user.Statistics[successfulCommandsKey] + 1;
        }

        // Track the last time this command was used
        await _commandUsageRepository.SetLastUsedAsync(commandName, DateTime.UtcNow);

        await _userRepository.CreateOrUpdateAsync(user);
    }

    public async Task<DateTime?> GetCommandLastUsedAsync(string commandName)
    {
        return await _commandUsageRepository.GetLastUsedAsync(commandName);
    }

    public async Task SetCommandLastUseAsync(string commandName, DateTime date)
    {
        await _commandUsageRepository.SetLastUsedAsync(commandName, date);
    }
}