using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Data;

namespace ButterBror.Application.Commands;

public class UserInfoCommand : ICommand
{
    public async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        try
        {
            var localization = serviceProvider.GetService<ILocalizationService>();
            var userRepository = serviceProvider.GetService<IUserRepository>();

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
            var errorTracking = serviceProvider.GetService<IErrorTrackingService>();
            return await errorTracking.LogErrorAsync(
                ex,
                "Failed to execute UserInfo",
                context.User.Id,
                context.Channel.Platform,
                context);
        }
    }
}