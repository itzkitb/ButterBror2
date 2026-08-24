using ButterBror.Core.Modules.Commands;

namespace ButterBror.Core.Modules.Interfaces;

/// <summary>
/// Unified command interface that receives only essential data
/// </summary>
public interface ICommand
{
    /// <summary>
    /// Execute command code
    /// </summary>
    /// <param name="context">Command execution context</param>
    /// <param name="serviceProvider">Service provider</param>
    /// <returns>Result of command execution</returns>
    Task<CommandResult> ExecuteAsync(
        CommandContext context,
        ICommandServiceProvider serviceProvider);
}