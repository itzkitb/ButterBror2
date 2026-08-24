using System.Security.Cryptography;
using System.Text;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Messaging;
using ButterBror.Core.Modules.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class CommandDispatcher(
    ILogger<CommandDispatcher> logger,
    ICommandRegistry commandRegistry,
    IServiceProvider provider,
    IErrorTrackingService errorTrackingService,
    ILocalizationService localization)
    : ICommandDispatcher
{
    private readonly IDashboardBridge _dashboardBridge = provider.GetRequiredService<IDashboardBridge>();

    public async Task<CommandResult> DispatchAsync(CommandContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // s0: obtaining the command factory
            var factory = commandRegistry.GetCommandFactory(context.CommandName, true);
            if (factory == null)
            {
                return CommandResult.Failure(
                    await localization.GetStringAsync("core.bot.command.not_found", context.Locale,
                        context.CommandName),
                    sendResult: false);
            }

            // s1: create a command instance
            var command = factory();

            // s2: create a context
            var serviceProvider = new CommandServiceProvider(provider);

            var result = await command.ExecuteAsync(context, serviceProvider);
            result.ExecutionTime = stopwatch.Elapsed;
            
            _dashboardBridge.IncrementCommandCount();

            return result;
        }
        catch (Exception ex)
        {
            var errorLog = await errorTrackingService.LogErrorAsync(
                ex,
                "error dispatching command",
                context.User.UnifiedId,
                context.PlatformId,
                context);

            return errorLog.Item1;
        }
        finally
        {
            stopwatch.Stop();
        }
    }
}
