using ButterBror.Core.Models.Commands;
using ButterBror.Infrastructure.Data;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ButterBror.Application.Commands;

public record UserInfoCommand(string TargetUsername) : ICommand;

public class UserInfoCommandHandler : IRequestHandler<UserInfoCommand, CommandResult>
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserInfoCommandHandler> _logger;

    public UserInfoCommandHandler(IUserRepository userRepository, ILogger<UserInfoCommandHandler> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<CommandResult> Handle(UserInfoCommand command, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Сначала ищем по индексу платформы (быстрый путь)
            var user = await _userRepository.GetByPlatformIdAsync("twitch", command.TargetUsername);

            // 2. Если не найдено - ищем по отображаемому имени
            if (user == null)
            {
                user = await _userRepository.GetByDisplayNameAsync(command.TargetUsername);
            }

            // 3. Если всё ещё не найдено - возвращаем ошибку
            if (user == null)
            {
                return CommandResult.Failure($"User '{command.TargetUsername}' not found. " +
                    "Try using exact username or Twitch ID.");
            }

            var stats = user.Statistics
                .Select(kvp => $"{kvp.Key}: {kvp.Value}")
                .ToList();

            return CommandResult.Successfully(
                $"User: {user.DisplayName}\n" +
                $"Unified ID: {user.UnifiedUserId}\n" +
                $"Platforms: {string.Join(", ", user.PlatformIds.Keys)}\n" +
                $"Statistics:\n{string.Join("\n", stats)}",
                user
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling UserInfoCommand for user '{TargetUsername}'", command.TargetUsername);
            return CommandResult.Failure($"Error retrieving user info: {ex.Message}");
        }
    }
}