using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.ChatModules.Twitch.Commands;

public class PartChannelCommand(IServiceProvider serviceProvider, ITwitchClient twitchClient, ITwitchChannelManager channelManager)
    : ICommand
{
    private readonly ILogger<PartChannelCommand> _logger = serviceProvider.GetRequiredService<ILogger<PartChannelCommand>>();
    private readonly IPermissionManager _permissionManager = serviceProvider.GetRequiredService<IPermissionManager>();
    private readonly ILocalizationService _localization = serviceProvider.GetRequiredService<ILocalizationService>();

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        ICommandServiceProvider serviceProvider)
    {
        // s0: checking for the presence of an argument
        if (context.Arguments.Count == 0)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.part.usage", context.Locale));
        }

        var channelName = context.Arguments[0].TrimStart('#').TrimStart('@').TrimEnd(',').ToLowerInvariant();
        var user = context.User;

        // s1: checking user permission
        var hasPermission = await _permissionManager.HasPermissionAsync(
            user.UnifiedId,
            "su:twitch:part"
        );

        if (!hasPermission)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.part.permission", context.Locale));
        }

        var channel = await twitchClient.ResolveChannelAsync(channelName);
        if (channel is null)
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.twitch.channel_not_found", context.Locale));
        await channelManager.RemoveChannelAsync(channel.Id);
        await twitchClient.LeaveChannelAsync(channel.Login);

        _logger.LogInformation("[tw] parted channel. chat={Channel}, user={User}",
            channelName, context.User.DisplayName);

        return CommandResult.Successfully(
            await _localization.GetStringAsync("command.part.success", context.Locale,
                channelName));
    }
}
