using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;

namespace ButterBror.Core.Interfaces;

public interface ICommandDispatcher
{
    Task<CommandResult> DispatchAsync(ICommandContext context);
}
