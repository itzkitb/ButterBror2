using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Core.Interfaces;

public interface ICommandProcessor
{
    Task<CommandResult> ProcessCommandAsync(ICommandContext context);
}