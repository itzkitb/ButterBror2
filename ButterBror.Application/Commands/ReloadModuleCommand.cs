using ButterBror.ChatModule;
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
        var localization = GetService<ILocalizationService>(serviceProvider);
        var moduleManager = GetService<IPlatformModuleManager>(serviceProvider);

        if (context.Arguments.Count < 2)
        {
            return CommandResult.Failure(
                await localization.GetStringAsync("command.module_reload.usage", context.Locale));
        }

        string type = context.Arguments[0].ToLowerInvariant();
        string moduleId = context.Arguments[1];

        try
        {
            string? result = type switch
            {
                "chat" => await moduleManager.ReloadChatModuleAsync(moduleId, context.CancellationToken),
                "command" => await moduleManager.ReloadCommandModuleAsync(moduleId, context.CancellationToken),
                _ => null
            };

            return result switch
            {
                null => CommandResult.Failure(
                    await localization.GetStringAsync("command.module_reload.unknown", context.Locale)),
                "error:not_found" => CommandResult.Failure(
                    await localization.GetStringAsync("command.module_reload.not_found", context.Locale)),
                "error:not_found_local" => CommandResult.Failure(
                    await localization.GetStringAsync("command.module_reload.not_found_local", context.Locale)),
                "success" => CommandResult.Successfully(
                    await localization.GetStringAsync("command.module_reload.success", context.Locale)),
                _ => CommandResult.Failure(
                    await localization.GetStringAsync("command.module_reload.exception", context.Locale))
            };
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
