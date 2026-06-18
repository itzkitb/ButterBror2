using ButterBror.ChatModules.Twitch.Models;
using ButterBror.ChatModules.Twitch.Services;
using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using ButterBror.Data;
using Microsoft.Extensions.Logging;

namespace ButterBror.ChatModules.Twitch.Commands;

public class PartChannelCommand : CommandBase
{
    private readonly ITwitchClient _twitchClient;

    public PartChannelCommand(ITwitchClient twitchClient)
    {
        _twitchClient = twitchClient;
    }

    public override async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        try
        {
            var logger = GetLogger<PartChannelCommand>(serviceProvider);
            var permissionManager = GetService<IPermissionManager>(serviceProvider);
            var userRepository = GetService<IUserRepository>(serviceProvider);
            var localization = GetService<ILocalizationService>(serviceProvider);

            // S0: Checking for the presence of an argument
            if (context.Arguments.Count == 0)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.part.usage", context.Locale));
            }

            var channelName = context.Arguments[0].TrimStart('#').TrimStart('@').TrimEnd(',').ToLowerInvariant();

            // S1: Get the user profile to obtain the unifiedUserId
            var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
            if (user == null)
            {
                throw new Exception("User not found");
            }

            // S2: Checking user permission
            var hasPermission = await permissionManager.HasPermissionAsync(
                user.UnifiedUserId,
                "su:twitch:part"
            );

            if (!hasPermission)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.part.permission", context.Locale));
            }

            // S3: Trying to disconnect from the channel
            await _twitchClient.LeaveChannelAsync(channelName);

            logger.LogInformation("Parted channel '{Channel}' by user '{User}'",
                channelName, context.User.DisplayName);

            return CommandResult.Successfully(
                await localization.GetStringAsync("command.part.success", context.Locale,
                    channelName));
        }
        catch (Exception ex)
        {
            var errorTracking = GetService<IErrorTrackingService>(serviceProvider);
            return await errorTracking.LogErrorAsync(
                ex,
                "Failed to execute PartChannel",
                context.User.Id,
                context.Channel.Platform,
                context);
        }
    }
}
