using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
using Microsoft.Extensions.Logging;

namespace ButterBror.Core.Abstractions;

/// <summary>
/// Base class for unified commands with common functionality
/// </summary>
public abstract class CommandBase : ICommand
{
    public abstract Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider);

    /// <summary>
    /// Helper method to get service from provider
    /// </summary>
    protected T GetService<T>(ICommandServiceProvider serviceProvider) where T : notnull
    {
        return serviceProvider.GetService<T>();
    }

    /// <summary>
    /// Helper method to get logger for command
    /// </summary>
    protected ILogger<T> GetLogger<T>(ICommandServiceProvider serviceProvider) where T : notnull
    {
        return GetService<ILogger<T>>(serviceProvider);
    }
}