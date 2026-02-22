using ButterBror.Core.Models;
using ButterBror.Core.Models.Commands;

namespace ButterBror.Infrastructure.Services;

public interface ICommandProcessor
{
    Task<CommandResult> ProcessCommandAsync(ICommandContext context);
}