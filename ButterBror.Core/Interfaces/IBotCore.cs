using ButterBror.Core.Models;
using ButterBror.Core.Models.Commands;

namespace ButterBror.Core.Interfaces;

public interface IBotCore
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<CommandResult> ProcessCommandAsync(ICommandContext context);
}
