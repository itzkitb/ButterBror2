using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Hosting;

namespace ButterBror.Infrastructure.Services;

public class DeviceStatsHostedService(IDeviceStatsService service) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ((DeviceStatsService)service).Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        ((DeviceStatsService)service).Stop();
        return Task.CompletedTask;
    }
}
