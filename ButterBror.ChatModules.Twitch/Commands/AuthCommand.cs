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
    private readonly ILogger<AuthCommand> _logger = serviceProvider.GetRequiredService<ILogger<AuthCommand>>();
    private ILocalizationService _localization = serviceProvider.GetRequiredService<ILocalizationService>();

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        ICommandServiceProvider serviceProvider)
    {
        var botAuthUrl = _config.RedirectUri;
        _logger.LogInformation("[tw] auth url generated. url={Url}", botAuthUrl);

        var response = await _localization.GetStringAsync(
            "command.auth.success",
            context.Locale,
            botAuthUrl);
        
        return CommandResult.Successfully(response);
    }
}