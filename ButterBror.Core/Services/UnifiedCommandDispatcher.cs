using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.Core.Services;

public class UnifiedCommandDispatcher : IUnifiedCommandDispatcher
{
    private readonly ILogger<UnifiedCommandDispatcher> _logger;
    private readonly ICommandTypeRegistry _commandTypeRegistry;

    public UnifiedCommandDispatcher(
        ILogger<UnifiedCommandDispatcher> logger,
        ICommandTypeRegistry commandTypeRegistry)
    {
        _logger = logger;
        _commandTypeRegistry = commandTypeRegistry;
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
            var commandType = _commandTypeRegistry.GetCommandType(commandName);
            if (commandType == null)
            {
                return CommandResult.Failure($"Command '{commandName}' not found", sendResult: false);
            }

            // Get the command instance from DI
            var command = services.GetService(commandType) as IUnifiedCommand;
            if (command == null)
            {
                return CommandResult.Failure($"Command '{commandName}' could not be instantiated", sendResult: false);
            }

            var result = await command.ExecuteAsync(channel, arguments, user, services);
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