using ButterBror.CommandModule.Commands;
using ButterBror.CommandModule.Context;

namespace ButterBror.Infrastructure.Services;

public interface ICommandProcessor
{
    Task<CommandResult> ProcessCommandAsync(ICommandContext context);
}