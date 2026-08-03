using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.ChatModules.Twitch.Commands;

public class JoinChannelCommand(
    IServiceProvider serviceProvider,
    ITwitchClient twitchClient)
    : ICommand
{
    private readonly ILogger<JoinChannelCommand> _logger = serviceProvider.GetRequiredService<ILogger<JoinChannelCommand>>();
    private readonly IPermissionManager _permissionManager = serviceProvider.GetRequiredService<IPermissionManager>();
    private readonly ILocalizationService _localization = serviceProvider.GetRequiredService<ILocalizationService>();

    public async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        // S0: Validate arguments
        if (context.Arguments.Count == 0)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.join.usage", context.Locale));
        }

        string channelName = context.Arguments[0].TrimStart('#').TrimStart('@').TrimEnd(',').ToLowerInvariant();

        // S1: Resolve user
        var user = context.User;

        // S2: Check permissions
        var hasPermission = await _permissionManager.HasPermissionAsync(
            user.UnifiedId,
            "su:twitch:join");

        if (!hasPermission)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.join.permission", context.Locale));
        }

        // S3: Join
        await twitchClient.JoinChannelAsync(channelName);
        _logger.LogInformation("Joined channel '{Channel}' by user '{User}'", channelName, context.User.DisplayName);

        return CommandResult.Successfully(
            await _localization.GetStringAsync("command.join.success", context.Locale,
                channelName));
    }
}