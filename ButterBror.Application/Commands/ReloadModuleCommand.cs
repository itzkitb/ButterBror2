using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using ButterBror.Data;
using ButterBror.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace ButterBror.Application.Commands;

public class ReloadModuleCommand : CommandBase
{
    public override async Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider)
    {
        var localizationService = GetService<ILocalizationService>(serviceProvider);
        var logger = GetLogger<ReloadModuleCommand>(serviceProvider);
        var moduleManager = GetService<IPlatformModuleManager>(serviceProvider);
        var userRepository = GetService<IUserRepository>(serviceProvider);

        var platform = context.Channel.Platform.ToLowerInvariant();
        var executor = await userRepository.GetByPlatformIdAsync(platform, context.User.Id);
        var executorLocale = executor != null ? executor.PreferredLocale : "EN_US";

        if (context.Arguments.Count < 2)
        {
            string locale = await localizationService.GetStringAsync("command.modulereload.usage", executorLocale);
            return CommandResult.Failure(locale);
        }

        string type = context.Arguments[0].ToLowerInvariant();
        string moduleId = context.Arguments[1];

        try
        {
            string result = type switch
            {
                "chat" => await moduleManager.ReloadChatModuleAsync(moduleId, context.CancellationToken),
                "command" => await moduleManager.ReloadCommandModuleAsync(moduleId, context.CancellationToken),
                _ => null!
            };

            if (result == null)
            {
                string locale = await localizationService.GetStringAsync("command.modulereload.unknown.type", executorLocale, type);
                return CommandResult.Failure(locale);
            }

            logger.LogInformation("Module reloaded via command: {Result}", result);
            return CommandResult.Successfully(result);
        }
        catch (Exception ex)
        {
            var errorTracking = GetService<IErrorTrackingService>(serviceProvider);
            return await errorTracking.LogErrorAsync(
                ex,
                "Failed to execute ReloadModuleCommand",
                context.User.Id,
                context.Channel.Platform,
                context);
        }
    }
}
