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
    private readonly IUnifiedCommandDispatcher _unifiedCommandDispatcher;
    private readonly IServiceProvider _serviceProvider;

    public CommandDispatcher(
        IMediator mediator,
        IUnifiedCommandDispatcher unifiedCommandDispatcher,
        IServiceProvider serviceProvider,
        ILogger<CommandDispatcher> logger)
    {
        _mediator = mediator;
        _unifiedCommandDispatcher = unifiedCommandDispatcher;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<CommandResult> DispatchAsync(ICommandContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Dispatch using the unified command dispatcher
            var result = await _unifiedCommandDispatcher.DispatchAsync(
                context.CommandName,
                context.Channel,
                context.Arguments.ToList(),
                context.User,
                _serviceProvider
            );
            
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