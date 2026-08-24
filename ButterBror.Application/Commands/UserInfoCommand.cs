using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Data;
using ButterBror.Data.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.Application.Commands;

public class UserInfoCommand(IServiceProvider serviceProvider) : ICommand
{
    private readonly ILocalizationService _localization = serviceProvider.GetRequiredService<ILocalizationService>();
    private readonly IUserRepository _userRepository = serviceProvider.GetRequiredService<IUserRepository>();

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        ICommandServiceProvider serviceProvider)
    {
        var platform = context.PlatformId.ToLowerInvariant();
        var targetUsername = context.Arguments.Count > 0
            ? context.Arguments[0]
            : context.User.DisplayName;

        var userEntity = await _userRepository.FindUserAsync(platform, targetUsername);

        if (userEntity == null)
            return CommandResult.Failure(
                await _localization.GetStringAsync("command.userinfo.not_found", context.Locale));

        return CommandResult.Successfully(
            await _localization.GetStringAsync("command.userinfo.success", context.Locale,
                userEntity.DisplayName,
                userEntity.UnifiedId));
    }
}