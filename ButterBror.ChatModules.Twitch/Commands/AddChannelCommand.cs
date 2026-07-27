using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Interfaces;
using ButterBror.Data;
using System.Text.Json;
using ButterBror.Core.Modules;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.ChatModules.Twitch.Commands;

/// <summary>
/// Adds a channel to the IRC or EventSub list and connects to it on the fly
/// </summary>
public class AddChannelCommand : CommandBase
{
    private readonly ITwitchClient _twitchClient;
    private readonly ITwitchChannelManager _channelManager;

    public AddChannelCommand(ITwitchClient twitchClient, ITwitchChannelManager channelManager)
    {
        _twitchClient = twitchClient;
        _channelManager = channelManager;
    }

    public override async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
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
                await localization.GetStringAsync("command.add_channel.usage", context.Locale));
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
            "su:twitch:addchannel");

        if (!hasPermission)
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("command.add_channel.permission", context.Locale));
        }

        // S3: Persist to Redis
        await _channelManager.AddChannelAsync(channelName);

        // S4: Connect on the fly
        await _twitchClient.AddChannelAsync(channelName);

        return CommandResult.Successfully(
            await localization.GetStringAsync("command.add_channel.success", context.Locale,
                channelName));
    }
}