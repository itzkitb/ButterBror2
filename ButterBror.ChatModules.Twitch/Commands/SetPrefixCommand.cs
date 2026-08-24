using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Data.Interfaces;
using ButterBror.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.ChatModules.Twitch.Commands;

public class SetPrefixCommand(IServiceProvider serviceProvider, TwitchModule module) : ICommand
{
    public static string GetPrefixKey(string channelId) => $"twitch:channel_prefix:{channelId}";
    private readonly ILogger<SetPrefixCommand> _logger = serviceProvider.GetRequiredService<ILogger<SetPrefixCommand>>();
    private readonly ICustomDataRepository _customRepo = serviceProvider.GetRequiredService<ICustomDataRepository>();
    private readonly ILocalizationService _localization = serviceProvider.GetRequiredService<ILocalizationService>();

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        ICommandServiceProvider serviceProvider)
    {
        // s0: checking user permission
        var hasPermission = context.PlatformPermissions.Contains(PlatformPermission.Owner) |
                            context.PlatformPermissions.Contains(PlatformPermission.Moderator);

        if (!hasPermission)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.part.permission", context.Locale));
        }
        
        // s1: validate argument
        if (context.Arguments.Count == 0)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.set_prefix.usage", context.Locale));
        }

        var newPrefix = context.Arguments[0];

        if (string.IsNullOrWhiteSpace(newPrefix))
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.set_prefix.empty", context.Locale));
        }

        if (newPrefix.Length > 32)
        {
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.set_prefix.32chars", context.Locale));
        }
        
        // s2: persist the new prefix
        var key = GetPrefixKey(context.Chat.Id);
        await _customRepo.SetDataAsync(key, newPrefix);

        // s3: cache
        module.InvalidatePrefixCache(context.Chat.Id);

        _logger.LogInformation(
            "[tw] channel prefix updated. channel={Channel}, cid={ChannelId}, prefix={Prefix}, user={User}",
            context.Chat.Name, context.Chat.Id, newPrefix, context.User.DisplayName);

        return CommandResult.Successfully(
            await _localization.GetStringAsync("command.set_prefix.success", context.Locale,
                context.Chat.Name,
                newPrefix));
    }
}