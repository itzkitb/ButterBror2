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
            var localization = GetService<ILocalizationService>(serviceProvider);

            // S0: Validate arguments
            if (context.Arguments.Count < 1)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.del_channel.usage", context.Locale));
            }
            var channelName = context.Arguments[0].TrimStart('#').TrimStart('@').TrimEnd(',').ToLowerInvariant();

            // S2: Check permissions
            var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
            if (user == null)
            {
                throw new Exception("User not found");
            }

            var hasPermission = await permissionManager.HasPermissionAsync(
                user.UnifiedUserId,
                "su:twitch:deletechannel");

            if (!hasPermission)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.del_channel.permission", context.Locale));
            }

            // S3: Persist to Redis
            var redisKey = "twitch:channels";
            var currentJson = await customData.GetDataAsync(redisKey) ?? "[]";
            var channels = JsonSerializer.Deserialize<List<string>>(currentJson) ?? new List<string>();

            if (!channels.Contains(channelName, StringComparer.OrdinalIgnoreCase))
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.del_channel.not_found", context.Locale,
                        channelName));
            }

            channels.Remove(channelName);
            await customData.SetDataAsync(redisKey, JsonSerializer.Serialize(channels));

            // S4: Connect on the fly
            await _twitchClient.LeaveChannelAsync(channelName);

            return CommandResult.Successfully(
                await localization.GetStringAsync("command.del_channel.success", context.Locale,
                    channelName));
        }
        catch (Exception ex)
        {
            var errorTracking = GetService<IErrorTrackingService>(serviceProvider);
            return await errorTracking.LogErrorAsync(
                ex,
                "Failed to execute DeleteChannel",
                context.User.Id,
                context.Channel.Platform,
                context);
        }
    }
}