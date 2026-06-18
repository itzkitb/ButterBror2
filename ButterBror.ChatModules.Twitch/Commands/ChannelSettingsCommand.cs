using System.Text.Json;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using ButterBror.Data;

namespace ButterBror.ChatModules.Twitch.Commands;

public class ChannelSettingsCommand : CommandBase
{
    private readonly ITwitchClient _client;

    public ChannelSettingsCommand(ITwitchClient twitchClient)
    {
        _client = twitchClient;
    }

    public override async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context, 
        ICommandServiceProvider serviceProvider)
    {
        try
        {
            var customData = GetService<ICustomDataRepository>(serviceProvider);
            var permissionManager = GetService<IPermissionManager>(serviceProvider);
            var userRepository = GetService<IUserRepository>(serviceProvider);
            var localization = GetService<ILocalizationService>(serviceProvider);

            // S0: Check permissions
            var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
            if (user == null)
            {
                throw new Exception("User not found");
            }

            var hasPermission = await permissionManager.HasPermissionAsync(
                user.UnifiedUserId,
                "su:twitch:settings");

            if (!hasPermission)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.channel_settings.permission", context.Locale));
            }

            // S1: Check args
            if (context.Arguments.Count < 2)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.channel_settings.usage", context.Locale));
            }

            string target = context.Arguments[0].ToLowerInvariant();
            if (!bool.TryParse(context.Arguments[1], out bool value))
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.channel_settings.value", context.Locale));
            }

            string channelId = context.Channel.Id;

            // S2: Changing
            var json = await customData.GetDataAsync($"twitch:settings:{channelId}");
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
                    await localization.GetStringAsync("command.channel_settings.unknown", context.Locale));
            }

            await customData.SetDataAsync($"twitch:settings:{channelId}", JsonSerializer.Serialize(settings));

            // S3: Reset cache
            _client.InvalidateChannelSettingsCache(channelId);

            return CommandResult.Successfully(
                await localization.GetStringAsync("command.channel_settings.unknown", context.Locale,
                    target,
                    value));
        }
        catch (Exception ex)
        {
            var errorTracking = GetService<IErrorTrackingService>(serviceProvider);
            return await errorTracking.LogErrorAsync(
                ex,
                "Failed to execute ChannelSettings",
                context.User.Id,
                context.Channel.Platform,
                context);
        }
    }
}