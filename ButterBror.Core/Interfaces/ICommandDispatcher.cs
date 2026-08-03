using ButterBror.Core.Models;
using ButterBror.Core.Modules.Commands;

namespace ButterBror.Core.Interfaces;

public interface ICommandDispatcher
{
    Task<CommandResult> DispatchAsync(ExtendedCommandContext context);
}
