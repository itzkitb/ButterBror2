using ButterBror.Core.Models;
using ButterBror.Core.Models.Commands;

namespace ButterBror.Core.Interfaces;

public interface ICommandDispatcher
{
    Task<CommandResult> DispatchAsync(ICommandContext context);
}
