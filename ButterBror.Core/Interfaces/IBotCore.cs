using ButterBror.Core.Models;

namespace ButterBror.Core.Interfaces;

public interface IBotCore
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<CommandResult> ProcessCommandAsync(ICommandContext context);
}
