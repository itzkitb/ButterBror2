using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Data;
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
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        // S0: Validate argument
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
        
        // S1: Persist the new prefix in Redis
        var key = GetPrefixKey(context.Channel.Id);
        await _customRepo.SetDataAsync(key, newPrefix);

        // S2: Cache
        module.InvalidatePrefixCache(context.Channel.Id);

        _logger.LogInformation(
            "[TW] Channel prefix updated. channel={Channel} ({ChannelId}), newPrefix={Prefix}, by={User}",
            context.Channel.Name, context.Channel.Id, newPrefix, context.User.DisplayName);

        return CommandResult.Successfully(
            await _localization.GetStringAsync("command.set_prefix.success", context.Locale,
                context.Channel.Name,
                newPrefix));
    }
}