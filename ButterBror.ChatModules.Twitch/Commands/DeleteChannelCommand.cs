using ButterBror.ChatModules.Twitch.Models;
using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using ButterBror.Data;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ButterBror.ChatModules.Twitch.Commands;

/// <summary>
/// Adds a channel to the IRC or EventSub list and connects to it on the fly
/// </summary>
public class DeleteChannelCommand : CommandBase
{
    private readonly ITwitchClient _twitchClient;

    public DeleteChannelCommand(ITwitchClient twitchClient)
    {
        _twitchClient = twitchClient;
    }

    public override async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        try
        {
            var logger = GetLogger<AddChannelCommand>(serviceProvider);
            var customData = GetService<ICustomDataRepository>(serviceProvider);
            var permissionManager = GetService<IPermissionManager>(serviceProvider);
            var userRepository = GetService<IUserRepository>(serviceProvider);

            // S0: Validate arguments
            if (context.Arguments.Count < 1)
            {
                return CommandResult.Failure("Usage: !delchannel <channel>. Example: !delchannel hasanabi");
            }
            var channelName = context.Arguments[0].TrimStart('#').TrimStart('@').TrimEnd(',').ToLowerInvariant();

            // S2: Check permissions
            var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
            if (user == null)
            {
                return CommandResult.Failure("User profile not found.");
            }

            var hasPermission = await permissionManager.HasPermissionAsync(
                user.UnifiedUserId,
                "su:twitch:deletechannel");

            if (!hasPermission)
            {
                return CommandResult.Failure("You don't have permission to delete channels");
            }

            // S3: Persist to Redis
            var redisKey = "twitch:channels";
            var currentJson = await customData.GetDataAsync(redisKey) ?? "[]";
            var channels = JsonSerializer.Deserialize<List<string>>(currentJson) ?? new List<string>();

            if (!channels.Contains(channelName, StringComparer.OrdinalIgnoreCase))
            {
                return CommandResult.Failure($"Channel #{channelName} not found in the list");
            }

            channels.Remove(channelName);
            await customData.SetDataAsync(redisKey, JsonSerializer.Serialize(channels));

            // S4: Connect on the fly
            await _twitchClient.LeaveChannelAsync(channelName);

            return CommandResult.Successfully($"Channel #{channelName} deleted from list and parted");
        }
        catch (Exception ex)
        {
            var logger = GetService<ILogger<AddChannelCommand>>(serviceProvider);
            logger.LogError(ex, "[TW] Error delete channel");
            throw;
        }
    }
}