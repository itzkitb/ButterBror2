using ButterBror.Core.Abstractions;
using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
using ButterBror.Data;
using Microsoft.Extensions.Logging;

namespace ButterBror.Application.Commands;

public class UserInfoCommand : CommandBase
{
    public override async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        try
        {
            var logger = GetLogger<UserInfoCommand>(serviceProvider);
            var userRepository = GetService<IUserRepository>(serviceProvider);

            // Determine target username - either from arguments or use caller's username
            var targetUsername = context.Arguments.Count > 0
                ? context.Arguments[0]
                : context.User.DisplayName;

            var platform = context.Channel.Platform.ToLowerInvariant();

            var userEntity = await userRepository.FindUserAsync(platform, targetUsername);

            if (userEntity == null)
            {
                return CommandResult.Failure($"User '{targetUsername}' not found. " +
                    "Try using exact username or platform ID.");
            }

            var stats = userEntity.Statistics
                .Select(kvp => $"{kvp.Key}: {kvp.Value}")
                .ToList();

            return CommandResult.Successfully(
                $"User: {userEntity.DisplayName}\n" +
                $"Internal id: {userEntity.UnifiedUserId}\n",
                userEntity
            );
        }
        catch (Exception ex)
        {
            var logger = GetService<ILogger<UserInfoCommand>>(serviceProvider);
            logger.LogError(ex, "Error handling UnifiedUserInfoCommand for user '{TargetUsername}'",
                context.Arguments.Count > 0 ? context.Arguments[0] : context.User.DisplayName);
            return CommandResult.Failure($"Error retrieving user info: {ex.Message}");
        }
    }
}