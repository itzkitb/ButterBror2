using ButterBror.ChatModules.Twitch.Interfaces;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.ChatModules.Twitch.Commands;

public class JoinChannelCommand(
    IServiceProvider serviceProvider,
    ITwitchClient twitchClient,
    ITwitchChannelManager channelManager,
    ITwitchNotificationService notificationService)
    : ICommand
{
    private readonly ILogger<JoinChannelCommand> _logger = serviceProvider.GetRequiredService<ILogger<JoinChannelCommand>>();
    private readonly IPermissionManager _permissionManager = serviceProvider.GetRequiredService<IPermissionManager>();
    private readonly ILocalizationService _localization = serviceProvider.GetRequiredService<ILocalizationService>();

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        ICommandServiceProvider serviceProvider)
    {
        // s0: validate arguments
        if (context.Arguments.Count == 0)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.join.usage", context.Locale));
        }

        var channelName = context.Arguments[0].TrimStart('#').TrimStart('@').TrimEnd(',').ToLowerInvariant();

        // s1: resolve user
        var user = context.User;

        // s2: check permissions
        var hasPermission = await _permissionManager.HasPermissionAsync(
            user.UnifiedId,
            "su:twitch:join");

        if (!hasPermission)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.join.permission", context.Locale));
        }

        var channel = await twitchClient.ResolveChannelAsync(channelName);
        if (channel is null)
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.twitch.channel_not_found", context.Locale));
        await channelManager.AddChannelAsync(channel);
        await twitchClient.JoinChannelAsync(channel.Login);
        await notificationService.NotifyChannelJoinedAsync(channel.Login, context.User.DisplayName, context.CancellationToken);
        _logger.LogInformation("[tw] joined channel '{Channel}' by user '{User}'", channelName, context.User.DisplayName);

        return CommandResult.Successfully(
            await _localization.GetStringAsync("command.join.success", context.Locale,
                channelName));
    }
}