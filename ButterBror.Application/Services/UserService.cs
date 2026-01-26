using ButterBror.Domain.Entities;
using ButterBror.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace ButterBror.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserService> _logger;

    public UserService(IUserRepository userRepository, ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing user service...");
        // Здесь можно добавить инициализацию, если нужно
    }

    public async Task<UserProfile> GetOrCreateUserAsync(string platformId, string platform, string displayName)
    {
        var user = await _userRepository.GetByPlatformIdAsync(platform, platformId);

        if (user != null)
        {
            user.LastActive = DateTime.UtcNow;
            user.DisplayName = displayName; // Обновляем displayName если изменился
            await _userRepository.CreateOrUpdateAsync(user);
            return user;
        }

        // Создаем нового пользователя
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

        // Обновляем статистику команд
        var commandKey = $"commands.{commandName}";
        if (!user.Statistics.ContainsKey(commandKey))
        {
            user.Statistics[commandKey] = 0;
        }

        user.Statistics[commandKey] = (int)user.Statistics[commandKey] + 1;

        // Обновляем общую статистику
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

        await _userRepository.CreateOrUpdateAsync(user);
    }
}