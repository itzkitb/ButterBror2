using ButterBror.ChatModules.Twitch.Models;
using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using ButterBror.Data;
using Microsoft.Extensions.Logging;

namespace ButterBror.ChatModules.Twitch.Commands;

public class SetPrefixCommand : CommandBase
{
    public static string GetPrefixKey(string channelId) => $"twitch:channel_prefix:{channelId}";
    private readonly TwitchModule _module;

    public SetPrefixCommand(TwitchModule module)
    {
        _module = module;
    }

    public override async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        try
        {
            var logger = GetLogger<SetPrefixCommand>(serviceProvider);
            var permissionManager = GetService<IPermissionManager>(serviceProvider);
            var userRepository = GetService<IUserRepository>(serviceProvider);
            var customData = GetService<ICustomDataRepository>(serviceProvider);

            // S0: Validate argument
            if (context.Arguments.Count == 0)
            {
                return CommandResult.Failure("Usage: <prefix> setprefix <new_prefix>. Example: ! setprefix ?");
            }

            var newPrefix = context.Arguments[0];

            if (string.IsNullOrWhiteSpace(newPrefix))
            {
                return CommandResult.Failure("Prefix cannot be empty or whitespace.");
            }

            if (newPrefix.Length > 32)
            {
                return CommandResult.Failure("Prefix must be 1-32 characters long.");
            }

            // S1: Resolve unified user
            var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
            if (user == null)
            {
                return CommandResult.Failure("User profile not found.");
            }

            // S2: Persist the new prefix in Redis
            var key = GetPrefixKey(context.Channel.Id);
            await customData.SetDataAsync(key, newPrefix);

            // S3: Fuck cache
            _module.InvalidatePrefixCache(context.Channel.Id);

            logger.LogInformation(
                "[TW] Channel prefix updated. channel={Channel} ({ChannelId}), newPrefix={Prefix}, by={User}",
                context.Channel.Name, context.Channel.Id, newPrefix, context.User.DisplayName);

            return CommandResult.Successfully(
                $"Command prefix for #{context.Channel.Name} has been changed to '{newPrefix}'.");
        }
        catch (Exception ex)
        {
            var logger = GetService<ILogger<SetPrefixCommand>>(serviceProvider);
            logger.LogError(ex, "[TW] Error changing prefix in channel {ChannelId}", context.Channel.Id);
            return CommandResult.Failure($"Failed to change prefix: {ex.Message}");
        }
    }
}