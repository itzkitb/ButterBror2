using ButterBror.ChatModules.Twitch.Models;
using ButterBror.ChatModules.Twitch.Services;
using ButterBror.Core.Abstractions;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
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

            // S0: Checking for the presence of an argument
            if (context.Arguments.Count == 0)
            {
                return CommandResult.Failure("Usage: !part <channel>. Specify the channel name to part.");
            }

            var channelName = context.Arguments[0].TrimStart('#').TrimStart('@').TrimEnd(',').ToLowerInvariant();

            // S1: Get the user profile to obtain the unifiedUserId
            var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
            if (user == null)
            {
                return CommandResult.Failure("User profile not found.");
            }

            // S2: Checking user permission
            var hasPermission = await permissionManager.HasPermissionAsync(
                user.UnifiedUserId,
                "su:twitch:part"
            );

            if (!hasPermission)
            {
                return CommandResult.Failure("You don't have permission to part channels. Required: su:twitch:part");
            }

            // S3: Check that the client supports part
            if (_twitchClient is not TwitchLibClient libClient)
            {
                return CommandResult.Failure("Current Twitch client doesn't support parting channels.");
            }

            // S4: Trying to disconnect from the channel
            await libClient.LeaveChannelAsync(channelName);

            logger.LogInformation("Parted channel '{Channel}' by user '{User}'",
                channelName, context.User.DisplayName);

            return CommandResult.Successfully($"Successfully parted channel '#{channelName}'.");
        }
        catch (Exception ex)
        {
            var logger = GetService<ILogger<PartChannelCommand>>(serviceProvider);
            logger.LogError(ex, "Error parting channel '{Channel}'",
                context.Arguments.Count > 0 ? context.Arguments[0] : "unknown");
            return CommandResult.Failure($"Error parting channel: {ex.Message}");
        }
    }
}
