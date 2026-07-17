using ButterBror.Core.Modules.Commands;

namespace ButterBror.Core.Modules.Interfaces;

/// <summary>
/// Unified command interface that receives only essential data
/// </summary>
public interface ICommand
{
    Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider);
}