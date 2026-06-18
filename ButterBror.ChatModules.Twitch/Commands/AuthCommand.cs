using ButterBror.ChatModules.Twitch.Models;
using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ButterBror.ChatModules.Twitch.Commands;

/// <summary>
/// Generates an authorization link for the broadcaster to authorize the bot on their channel
/// </summary>
public class AuthCommand : CommandBase
{
    private readonly TwitchConfiguration _config;

    public AuthCommand(IOptions<TwitchConfiguration> config)
    {
        _config = config.Value;
    }

    public override async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        try
        {
            var logger = GetLogger<AuthCommand>(serviceProvider);
            var localization = GetService<ILocalizationService>(serviceProvider);

            if (string.IsNullOrWhiteSpace(_config.ClientId))
            {
                throw new Exception("ClientId is not configured");
            }

            var botAuthUrl = $"{_config.RedirectUri}?client_id={_config.ClientId}&bot_username={_config.BotUsername}";
            logger.LogInformation("[TW] Auth URL generated. url={Url}", botAuthUrl);

            var response = await localization.GetStringAsync("", context.Locale, _config.BotUsername, botAuthUrl);
            return CommandResult.Successfully(response);
        }
        catch (Exception ex)
        {
            var errorTracking = GetService<IErrorTrackingService>(serviceProvider);
            return await errorTracking.LogErrorAsync(
                ex,
                "Failed to execute Auth",
                context.User.Id,
                context.Channel.Platform,
                context);
        }
    }
}