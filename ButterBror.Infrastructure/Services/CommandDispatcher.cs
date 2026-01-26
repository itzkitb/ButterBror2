using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using ButterBror.Core.Models.Commands;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class CommandDispatcher : ICommandDispatcher
{
    private readonly IMediator _mediator;
    private readonly ILogger<CommandDispatcher> _logger;
    private readonly ICommandParser _commandParser;

    public CommandDispatcher(
        IMediator mediator,
        ICommandParser commandParser,
        ILogger<CommandDispatcher> logger)
    {
        _mediator = mediator;
        _commandParser = commandParser;
        _logger = logger;
    }

    public async Task<CommandResult> DispatchAsync(ICommandContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Парсим команду в конкретный тип команды
            var command = _commandParser.ParseCommand(context);

            if (command == null)
            {
                return CommandResult.Failure($"Command '{context.CommandName}' not found");
            }

            // Используем MediatR для обработки команды
            var result = await _mediator.Send(command);
            result.ExecutionTime = stopwatch.Elapsed;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching command '{CommandName}'", context.CommandName);
            return CommandResult.Failure($"Internal error executing command: {ex.Message}");
        }
    }
}