using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using ButterBror.Data;

namespace ButterBror.Application.Commands;

public class UserInfoCommand : CommandBase
{
    public override async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        try
        {
            var localizationService = GetService<ILocalizationService>(serviceProvider);
            var logger = GetLogger<UserInfoCommand>(serviceProvider);
            var userRepository = GetService<IUserRepository>(serviceProvider);

            var platform = context.Channel.Platform.ToLowerInvariant();
            var executor = await userRepository.GetByPlatformIdAsync(platform, context.User.Id);
            var executorLocale = executor != null ? executor.PreferredLocale : "EN_US";

            // Determine target username - either from arguments or use caller's username
            var targetUsername = context.Arguments.Count > 0
                ? context.Arguments[0]
                : context.User.DisplayName;

            var userEntity = await userRepository.FindUserAsync(platform, targetUsername);

            if (userEntity == null)
            {
                var notFound = await localizationService.GetStringAsync("command.userinfo.error.user_not_found", executorLocale);
                return CommandResult.Failure(notFound);
            }

            var result = await localizationService.GetStringAsync("command.userinfo.result", executorLocale, userEntity.DisplayName, userEntity.UnifiedUserId);
            return CommandResult.Successfully(result);
        }
        catch (Exception ex)
        {
            var errorTracking = GetService<IErrorTrackingService>(serviceProvider);
            return await errorTracking.LogErrorAsync(
                ex,
                "Failed to execute UserInfo",
                context.User.Id,
                context.Channel.Platform,
                context);
        }
    }
}