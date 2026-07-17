using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Core.Interfaces;

public interface ICommandDispatcher
{
    Task<CommandResult> DispatchAsync(ICommandContext context);
}
