using ButterBror.CommandModule.Context;
using Microsoft.Extensions.Logging;

namespace ButterBror.CommandModule.Commands;

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