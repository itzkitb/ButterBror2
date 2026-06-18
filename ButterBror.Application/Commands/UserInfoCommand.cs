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
            var localization = GetService<ILocalizationService>(serviceProvider);
            var userRepository = GetService<IUserRepository>(serviceProvider);

            var platform = context.Channel.Platform.ToLowerInvariant();
            var targetUsername = context.Arguments.Count > 0
                ? context.Arguments[0]
                : context.User.DisplayName;

            var userEntity = await userRepository.FindUserAsync(platform, targetUsername);

            if (userEntity == null)
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.userinfo.not_found", context.Locale));
            
            return CommandResult.Successfully(
                await localization.GetStringAsync("command.userinfo.success", context.Locale,
                    userEntity.DisplayName,
                    userEntity.UnifiedUserId));
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