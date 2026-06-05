using ButterBror.ChatModules.Twitch.Models;
using ButterBror.ChatModules.Twitch.Services;
using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
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

            // S0: Validate arguments
            if (context.Arguments.Count == 0)
            {
                return CommandResult.Failure("Usage: !join <channel>.");
            }

            string channelName = context.Arguments[0].TrimStart('#').TrimStart('@').TrimEnd(',').ToLowerInvariant();

            // S1: Resolve user
            var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
            if (user == null)
            {
                return CommandResult.Failure("User profile not found.");
            }

            // S2: Check permissions
            var hasPermission = await permissionManager.HasPermissionAsync(
                user.UnifiedUserId,
                "su:twitch:join");

            if (!hasPermission)
            {
                return CommandResult.Failure("You don't have permission to join channels. Required: su:twitch:join");
            }

            // S3: Join
            await _twitchClient.JoinChannelAsync(channelName);

            logger.LogInformation(
                "Joined channel '{Channel}' by user '{User}'",
                channelName, context.User.DisplayName);

            return CommandResult.Successfully(
                $"Successfully joined channel #{channelName}.");
        }
        catch (Exception ex)
        {
            var logger = GetService<ILogger<JoinChannelCommand>>(serviceProvider);
            logger.LogError(ex, "Error joining channel");
            return CommandResult.Failure($"Error joining channel: {ex.Message}");
        }
    }
}