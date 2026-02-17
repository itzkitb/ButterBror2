using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class UnifiedCommandDispatcher : IUnifiedCommandDispatcher
{
    private readonly ILogger<UnifiedCommandDispatcher> _logger;
    private readonly IUnifiedCommandRegistry _commandRegistry;

    public UnifiedCommandDispatcher(
        ILogger<UnifiedCommandDispatcher> logger,
        IUnifiedCommandRegistry commandRegistry)
    {
        _logger = logger;
        _commandRegistry = commandRegistry;
    }

    public async Task<CommandResult> DispatchAsync(
        string commandName,
        IPlatformChannel channel,
        List<string> arguments,
        IPlatformUser user,
        IServiceProvider services)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // S0: Obtaining the command factory from the registry
            var factory = _commandRegistry.GetCommandFactory(commandName);
            if (factory == null)
            {
                return CommandResult.Failure($"Command '{commandName}' not found", sendResult: false);
            }

            // S1: Create a command instance through a factory
            var command = factory();

            // S2: Create an execution context and service provider
            var context = new CommandExecutionContext(channel, arguments, user);
            var serviceProvider = new CommandServiceProvider(services);

            var result = await command.ExecuteAsync(context, serviceProvider);
            result.ExecutionTime = stopwatch.Elapsed;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching unified command '{CommandName}'", commandName);
            return CommandResult.Failure($"Internal error executing command: {ex.Message}");
        }
    }
}
