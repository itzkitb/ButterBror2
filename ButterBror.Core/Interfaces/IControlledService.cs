namespace ButterBror.Core.Interfaces;

public interface IControlledService
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}