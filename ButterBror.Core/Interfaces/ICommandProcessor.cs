using ButterBror.Core.Modules.Commands;
using ButterBror.Core.Modules.Interfaces;

namespace ButterBror.Infrastructure.Services;

public interface ICommandProcessor
{
    Task<CommandResult> ProcessCommandAsync(ICommandContext context);
}