using ButterBror.ChatModules.Twitch.Models;
using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
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

    public override Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        try
        {
            var logger = GetLogger<AuthCommand>(serviceProvider);

            if (string.IsNullOrWhiteSpace(_config.ClientId))
            {
                return Task.FromResult(CommandResult.Failure("ClientId is not configured."));
            }

            var botAuthUrl =
                _config.RedirectUri +
                $"?client_id={_config.ClientId}" +
                $"&bot_username={_config.BotUsername}";

            logger.LogInformation("[TW] Auth URL generated. url={Url}", botAuthUrl);

            var response =
                $"🔐 | {botAuthUrl} ▹ " +
                $"After authorization, copy the code and send it to me via whisper (/w {_config.BotUsername} <code>)";

            return Task.FromResult(CommandResult.Successfully(response));
        }
        catch (Exception ex)
        {
            var logger = GetService<ILogger<AuthCommand>>(serviceProvider);
            logger.LogError(ex, "[TW] Failed to generate auth URL");
            return Task.FromResult(CommandResult.Failure($"Failed to generate auth URL: {ex.Message}"));
        }
    }
}