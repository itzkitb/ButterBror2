using ButterBror.Core.Models;
using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Command dispatcher
/// </summary>
public interface ICommandDispatcher
{
    /// <summary>
    /// Dispatch command
    /// </summary>
    /// <param name="context">Command execution context</param>
    /// <returns></returns>
    Task<CommandResult> DispatchAsync(CommandContext context);
}
