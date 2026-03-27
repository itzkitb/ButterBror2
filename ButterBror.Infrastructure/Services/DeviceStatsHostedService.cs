using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Hosting;

namespace ButterBror.Infrastructure.Services;

public class DeviceStatsHostedService : IHostedService
{
    private readonly IDeviceStatsService _service;

    public DeviceStatsHostedService(IDeviceStatsService service)
    {
        _service = service;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ((DeviceStatsService)_service).Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        ((DeviceStatsService)_service).Stop();
        return Task.CompletedTask;
    }
}
