using ButterBror.ChatModules.Twitch.Models;
using ButterBror.ChatModules.Twitch.Services;
using ButterBror.Core.Abstractions;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
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

            // S0: Checking for the presence of an argument
            if (context.Arguments.Count == 0)
            {
                return CommandResult.Failure("Usage: !join <channel>. Specify the channel name to join.");
            }

            var channelName = context.Arguments[0].TrimStart('#').TrimStart('@').TrimEnd(',').ToLowerInvariant();

            // S1: Get the user profile to obtain the unifiedUserId
            var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
            if (user == null)
            {
                return CommandResult.Failure("User profile not found.");
            }

            // S2: Checking user rights
            var hasPermission = await permissionManager.HasPermissionAsync(
                user.UnifiedUserId,
                "su:twitch:join"
            );

            if (!hasPermission)
            {
                return CommandResult.Failure("You don't have permission to join channels. Required: su:twitch:join");
            }

            // S3: Check that the client supports join
            if (_twitchClient is not TwitchLibClient libClient)
            {
                return CommandResult.Failure("Current Twitch client doesn't support joining channels.");
            }

            // S4: Trying to connect to the channel
            await libClient.JoinChannelAsync(channelName);

            logger.LogInformation("Joined channel '{Channel}' by user '{User}'",
                channelName, context.User.DisplayName);

            return CommandResult.Successfully($"Successfully joined channel '#{channelName}'.");
        }
        catch (Exception ex)
        {
            var logger = GetService<ILogger<JoinChannelCommand>>(serviceProvider);
            logger.LogError(ex, "Error joining channel '{Channel}'",
                context.Arguments.Count > 0 ? context.Arguments[0] : "unknown");
            return CommandResult.Failure($"Error joining channel: {ex.Message}");
        }
    }
}
