using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.ChatModules.Twitch.Commands;

public class DeleteChannelCommand(
    IServiceProvider serviceProvider,
    ITwitchClient twitchClient,
    ITwitchChannelManager channelManager)
    : ICommand
{
    private readonly IPermissionManager _permissionManager = serviceProvider.GetRequiredService<IPermissionManager>();
    private readonly ILocalizationService _localization = serviceProvider.GetRequiredService<ILocalizationService>();

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        ICommandServiceProvider serviceProvider)
    {
        // s0: validate arguments
        if (context.Arguments.Count < 1)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.del_channel.usage", context.Locale));
        }

        var channelName = context.Arguments[0].TrimStart('#').TrimStart('@').TrimEnd(',').ToLowerInvariant();

        // s2: check permissions
        var user = context.User;
        var hasPermission = await _permissionManager.HasPermissionAsync(
            user.UnifiedId,
            "su:twitch:deletechannel");

        if (!hasPermission)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.del_channel.permission", context.Locale));
        }

        // s3: persist to Redis
        await channelManager.RemoveChannelAsync(channelName);

        // s4: connect on the fly
        await twitchClient.LeaveChannelAsync(channelName);

        return CommandResult.Successfully(
            await _localization.GetStringAsync("command.del_channel.success", context.Locale,
                channelName));
    }
}