using System.Text.Json;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.ChatModules.Twitch.Commands;

public class ChannelSettingsCommand(IServiceProvider serviceProvider, ITwitchClient twitchClient)
    : ICommand
{
    private readonly ICustomDataRepository _customRepo = serviceProvider.GetRequiredService<ICustomDataRepository>();
    private readonly IPermissionManager _permissionManager = serviceProvider.GetRequiredService<IPermissionManager>();
    private readonly ILocalizationService _localization = serviceProvider.GetRequiredService<ILocalizationService>();

    public async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        // S0: Check permissions
        var user = context.User;
        var hasPermission = await _permissionManager.HasPermissionAsync(
            user.UnifiedId,
            "su:twitch:settings");

        if (!hasPermission)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.channel_settings.permission", context.Locale));
        }

        // S1: Check args
        if (context.Arguments.Count < 2)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.channel_settings.usage", context.Locale));
        }

        string target = context.Arguments[0].ToLowerInvariant();
        if (!bool.TryParse(context.Arguments[1], out bool value))
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.channel_settings.value", context.Locale));
        }

        string channelId = context.Channel.Id;

        // S2: Changing
        var json = await _customRepo.GetDataAsync($"twitch:settings:{channelId}");
        var settings = !string.IsNullOrWhiteSpace(json)
            ? JsonSerializer.Deserialize<TwitchChannelSettings>(json)
            : new TwitchChannelSettings();

        if (target == "online")
        {
            settings!.AllowOnline = value;
        }
        else if (target == "offline")
        {
            settings!.AllowOffline = value;
        }
        else
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.channel_settings.unknown", context.Locale));
        }

        await _customRepo.SetDataAsync($"twitch:settings:{channelId}", JsonSerializer.Serialize(settings));

        // S3: Reset cache
        twitchClient.InvalidateChannelSettingsCache(channelId);

        return CommandResult.Successfully(
            await _localization.GetStringAsync("command.channel_settings.unknown", context.Locale,
                target,
                value));
    }
}