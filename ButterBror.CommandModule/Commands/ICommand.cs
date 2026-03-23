using ButterBror.CommandModule.Context;

namespace ButterBror.CommandModule.Commands;

/// <summary>
/// Unified command interface that receives only essential data
/// </summary>
public interface ICommand
{
    Task<CommandResult> ExecuteAsync(
        ICommandExecutionContext context,
        ICommandServiceProvider serviceProvider);
}