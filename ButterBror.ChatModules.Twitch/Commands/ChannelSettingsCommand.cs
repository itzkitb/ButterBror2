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
        var customData = GetService<ICustomDataRepository>(serviceProvider);
        var permissionManager = GetService<IPermissionManager>(serviceProvider);
        var userRepository = GetService<IUserRepository>(serviceProvider);

        // S0: Check permissions
        var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
        if (user == null)
        {
            return CommandResult.Failure("User profile not found");
        }

        var hasPermission = await permissionManager.HasPermissionAsync(
            user.UnifiedUserId,
            "su:twitch:settings");

        if (!hasPermission)
        {
            return CommandResult.Failure("You don't have permission to add channels");
        }

        // S1: Check args
        if (context.Arguments.Count < 2)
        {
            return CommandResult.Failure("Usage: !twitchset <online|offline> <true|false>");
        }

        string target = context.Arguments[0].ToLowerInvariant();
        if (!bool.TryParse(context.Arguments[1], out bool value))
        {
            return CommandResult.Failure("The value must be true or false");
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
            return CommandResult.Failure("Unknown parameter. Available: online, offline");
        }

        await customData.SetDataAsync($"twitch:settings:{channelId}", JsonSerializer.Serialize(settings));
        
        // S3: Reset cache
        _client.InvalidateChannelSettingsCache(channelId);

        return CommandResult.Successfully($"Parameter '{target}' changed to {value}");
    }
}