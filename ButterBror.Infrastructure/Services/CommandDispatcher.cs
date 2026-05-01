using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class CommandDispatcher : ICommandDispatcher
{
    private readonly ILogger<CommandDispatcher> _logger;
    private readonly ICommandRegistry _commandRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDashboardBridge? _dashboardBridge;

    public CommandDispatcher(
        ILogger<CommandDispatcher> logger,
        ICommandRegistry commandRegistry,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _commandRegistry = commandRegistry;
        _serviceProvider = serviceProvider;
        _dashboardBridge = serviceProvider.GetService<IDashboardBridge>();
    }

    public async Task<CommandResult> DispatchAsync(ICommandContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // S0: Obtaining the command factory from the registry
            var factory = _commandRegistry.GetCommandFactory(context.CommandName);
            if (factory == null)
            {
                return CommandResult.Failure($"Command '{context.CommandName}' not found", sendResult: false);
            }

            // S1: Create a command instance through a factory
            var command = factory();

            // S2: Create an execution context and service provider
            var locale = (context as ExtendedCommandContext)?.Locale ?? "EN_US";
            var commandContext = new CommandExecutionContext(context.Channel, context.Arguments.ToList(), context.User, locale, context.CommandName);
            var serviceProvider = new CommandServiceProvider(_serviceProvider);

            var result = await command.ExecuteAsync(commandContext, serviceProvider);
            result.ExecutionTime = stopwatch.Elapsed;

            // Notify dashboard about executed command
            _dashboardBridge?.IncrementCommandCount();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching command. name='{CommandName}'", context.CommandName);
            return CommandResult.Failure($"Internal error executing command: {ex.Message}");
        }
    }
}
