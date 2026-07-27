using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ButterBror.ChatModules.Twitch.Commands;

public class AuthCommand(IServiceProvider serviceProvider, IOptions<TwitchConfiguration> config)
    : ICommand
{
    private readonly TwitchConfiguration _config = config.Value;
    private readonly Logger<AuthCommand> _logger = serviceProvider.GetRequiredService<Logger<AuthCommand>>();
    private ILocalizationService _localization = serviceProvider.GetRequiredService<ILocalizationService>();

    public async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(_config.ClientId))
        {
            throw new Exception("ClientId is not configured");
        }

        var botAuthUrl = $"{_config.RedirectUri}?client_id={_config.ClientId}&bot_username={_config.BotUsername}";
        _logger.LogInformation("[TW] Auth URL generated. url={Url}", botAuthUrl);

        var response = await _localization.GetStringAsync(
            "command.auth.success",
            context.Locale,
            _config.BotUsername,
            botAuthUrl);
        
        _logger.LogInformation("[TW] Result auth message: {res}", response);
        return CommandResult.Successfully(response);
    }
}