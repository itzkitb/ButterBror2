namespace ButterBror.Core.Interfaces;

public interface IDashboardService
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}