using System.Text.Json;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Data;
using ButterBror.Data.Interfaces;
using ButterBror.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.ChatModules.Twitch.Commands;

public class ChannelSettingsCommand(IServiceProvider serviceProvider, ITwitchClient twitchClient)
    : ICommand
{
    private readonly ICustomDataRepository _customRepo = serviceProvider.GetRequiredService<ICustomDataRepository>();
    private readonly IPermissionManager _permissionManager = serviceProvider.GetRequiredService<IPermissionManager>();
    private readonly ILocalizationService _localization = serviceProvider.GetRequiredService<ILocalizationService>();

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        ICommandServiceProvider serviceProvider)
    {
        // s0: check permissions
        var hasPermission = context.PlatformPermissions.Contains(PlatformPermission.Owner) |
                            context.PlatformPermissions.Contains(PlatformPermission.Moderator);
        if (!hasPermission)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.channel_settings.permission", context.Locale));
        }

        // s1: check args
        if (context.Arguments.Count < 2)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.channel_settings.usage", context.Locale));
        }

        var target = context.Arguments[0].ToLowerInvariant();
        if (!bool.TryParse(context.Arguments[1], out var value))
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.channel_settings.value", context.Locale));
        }

        var channelId = context.Chat.Id;

        // s2: changing
        var json = await _customRepo.GetDataAsync($"twitch:settings:{channelId}");
        var settings = !string.IsNullOrWhiteSpace(json)
            ? JsonSerializer.Deserialize<TwitchChannelSettings>(json)
            : new TwitchChannelSettings();

        switch (target)
        {
            case "online":
                settings!.AllowOnline = value;
                break;
            case "offline":
                settings!.AllowOffline = value;
                break;
            default:
                return CommandResult.Failure(
                    await _localization.GetStringAsync("command.channel_settings.unknown", context.Locale));
        }

        await _customRepo.SetDataAsync($"twitch:settings:{channelId}", JsonSerializer.Serialize(settings));

        // s3: reset cache
        twitchClient.InvalidateChannelSettingsCache(channelId);

        return CommandResult.Successfully(
            await _localization.GetStringAsync("command.channel_settings.unknown", context.Locale,
                target,
                value));
    }
}