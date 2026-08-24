using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Command processor
/// </summary>
public interface ICommandProcessor
{
    /// <summary>
    /// Process command
    /// </summary>
    /// <param name="context">Cancellation token</param>
    /// <returns>Result of command execution</returns>
    Task<CommandResult> ProcessCommandAsync(CommandContext context);
}