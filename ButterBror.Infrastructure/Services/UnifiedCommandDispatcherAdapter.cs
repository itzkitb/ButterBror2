using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using ButterBror.Core.Models.Commands;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class UnifiedCommandDispatcherAdapter : ICommandDispatcher
{
    private readonly IUnifiedCommandDispatcher _unifiedCommandDispatcher;
    private readonly ICommandRegistry _commandRegistry; // This is for metadata, not for unified commands
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UnifiedCommandDispatcherAdapter> _logger;

    public UnifiedCommandDispatcherAdapter(
        IUnifiedCommandDispatcher unifiedCommandDispatcher,
        ICommandRegistry commandRegistry, // This should be the old registry for metadata
        IServiceProvider serviceProvider,
        ILogger<UnifiedCommandDispatcherAdapter> logger)
    {
        _unifiedCommandDispatcher = unifiedCommandDispatcher;
        _commandRegistry = commandRegistry;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<CommandResult> DispatchAsync(ICommandContext context)
    {
        // Convert the context to the unified command format
        var arguments = context.Arguments.ToList();
        
        return await _unifiedCommandDispatcher.DispatchAsync(
            context.CommandName,
            context.Channel,
            arguments,
            context.User,
            _serviceProvider
        );
    }
}