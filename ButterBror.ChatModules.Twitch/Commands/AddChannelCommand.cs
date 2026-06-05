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
public class AddChannelCommand : CommandBase
{
    private readonly ITwitchClient _twitchClient;

    public AddChannelCommand(ITwitchClient twitchClient)
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
                return CommandResult.Failure("Usage: !addchannel <channel>. Example: !addchannel itzkitb");
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
                "su:twitch:join");

            if (!hasPermission)
            {
                return CommandResult.Failure("You don't have permission to add channels. Required: su:twitch:join");
            }

            // S3: Persist to Redis (IRC only)
            var redisKey = "twitch:irc_channels";
            var currentJson = await customData.GetDataAsync(redisKey) ?? "[]";
            var channels = JsonSerializer.Deserialize<List<string>>(currentJson) ?? new List<string>();

            if (channels.Contains(channelName, StringComparer.OrdinalIgnoreCase))
            {
                return CommandResult.Failure($"Channel #{channelName} is already in the list.");
            }

            channels.Add(channelName);
            await customData.SetDataAsync(redisKey, JsonSerializer.Serialize(channels));

            // S4: Connect on the fly
            await _twitchClient.JoinChannelAsync(channelName);

            return CommandResult.Successfully($"Channel #{channelName} added to list and connected.");
        }
        catch (Exception ex)
        {
            var logger = GetService<ILogger<AddChannelCommand>>(serviceProvider);
            logger.LogError(ex, "[TW] Error adding channel");
            return CommandResult.Failure($"Error adding channel: {ex.Message}");
        }
    }
}