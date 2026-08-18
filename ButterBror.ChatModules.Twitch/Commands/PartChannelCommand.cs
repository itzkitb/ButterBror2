using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.ChatModules.Twitch.Commands;

public class PartChannelCommand(IServiceProvider serviceProvider, ITwitchClient twitchClient)
    : ICommand
{
    private readonly ILogger<PartChannelCommand> _logger = serviceProvider.GetRequiredService<ILogger<PartChannelCommand>>();
    private readonly IPermissionManager _permissionManager = serviceProvider.GetRequiredService<IPermissionManager>();
    private readonly ILocalizationService _localization = serviceProvider.GetRequiredService<ILocalizationService>();

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        ICommandServiceProvider serviceProvider)
    {
        // S0: Checking for the presence of an argument
        if (context.Arguments.Count == 0)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.part.usage", context.Locale));
        }

        var channelName = context.Arguments[0].TrimStart('#').TrimStart('@').TrimEnd(',').ToLowerInvariant();

        // S1: Get the user profile to obtain the unifiedUserId
        var user = context.User;

        // S2: Checking user permission
        var hasPermission = await _permissionManager.HasPermissionAsync(
            user.UnifiedId,
            "su:twitch:part"
        );

        if (!hasPermission)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.part.permission", context.Locale));
        }

        // S3: Trying to disconnect from the channel
        await twitchClient.LeaveChannelAsync(channelName);

        _logger.LogInformation("Parted channel '{Channel}' by user '{User}'",
            channelName, context.User.DisplayName);

        return CommandResult.Successfully(
            await _localization.GetStringAsync("command.part.success", context.Locale,
                channelName));
    }
}
