using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
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
            var localization = GetService<ILocalizationService>(serviceProvider);

            // S0: Validate argument
            if (context.Arguments.Count == 0)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.set_prefix.usage", context.Locale));
            }

            var newPrefix = context.Arguments[0];

            if (string.IsNullOrWhiteSpace(newPrefix))
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.set_prefix.empty", context.Locale));
            }

            if (newPrefix.Length > 32)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("command.set_prefix.32chars", context.Locale));
            }

            // S1: Resolve unified user
            var user = await userRepository.GetByPlatformIdAsync(context.User.Platform, context.User.Id);
            if (user == null)
            {
                throw new Exception("User not found");
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
                await localization.GetStringAsync("command.set_prefix.success", context.Locale,
                    context.Channel.Name,
                    newPrefix));
        }
        catch (Exception ex)
        {
            var errorTracking = GetService<IErrorTrackingService>(serviceProvider);
            return await errorTracking.LogErrorAsync(
                ex,
                "Failed to execute SetPrefix",
                context.User.Id,
                context.Channel.Platform,
                context);
        }
    }
}