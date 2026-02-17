using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using ButterBror.Core.Models.Commands;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class UnifiedCommandDispatcherAdapter : ICommandDispatcher
{
    private readonly IUnifiedCommandDispatcher _unifiedCommandDispatcher;
    private readonly IServiceProvider _serviceProvider;

    public UnifiedCommandDispatcherAdapter(
        IUnifiedCommandDispatcher unifiedCommandDispatcher,
        IServiceProvider serviceProvider)
    {
        _unifiedCommandDispatcher = unifiedCommandDispatcher;
        _serviceProvider = serviceProvider;
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