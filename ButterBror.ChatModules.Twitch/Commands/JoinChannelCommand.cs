using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Data;
using Microsoft.Extensions.Logging;

namespace ButterBror.ChatModules.Twitch.Commands;

public class JoinChannelCommand : CommandBase
{
    private readonly ITwitchClient _twitchClient;

    public JoinChannelCommand(ITwitchClient twitchClient)
    {
        _twitchClient = twitchClient;
    }

    public override async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        try
        {
            var logger = GetLogger<JoinChannelCommand>(serviceProvider);
            var permissionManager = GetService<IPermissionManager>(serviceProvider);
            var userRepository = GetService<IUserRepository>(serviceProvider);
            var localization = GetService<ILocalizationService>(serviceProvider);

            // S0: Validate arguments
            if (context.Arguments.Count == 0)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.join.usage", context.Locale));
            }

            string channelName = context.Arguments[0].TrimStart('#').TrimStart('@').TrimEnd(',').ToLowerInvariant();

            // S1: Resolve user
            var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
            if (user == null)
            {
                throw new Exception("User not found"); 
            }

            // S2: Check permissions
            var hasPermission = await permissionManager.HasPermissionAsync(
                user.UnifiedUserId,
                "su:twitch:join");

            if (!hasPermission)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.join.permission", context.Locale));
            }

            // S3: Join
            await _twitchClient.JoinChannelAsync(channelName);
            logger.LogInformation("Joined channel '{Channel}' by user '{User}'", channelName, context.User.DisplayName);

            return CommandResult.Successfully(
                await localization.GetStringAsync("command.join.success", context.Locale,
                    channelName));
        }
        catch (Exception ex)
        {
            var errorTracking = GetService<IErrorTrackingService>(serviceProvider);
            return await errorTracking.LogErrorAsync(
                ex,
                "Failed to execute JoinChannel",
                context.User.Id,
                context.Channel.Platform,
                context);
        }
    }
}