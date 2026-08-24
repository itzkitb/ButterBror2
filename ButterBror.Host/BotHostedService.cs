using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ButterBror.Host;

public class BotHostedService(IBotCore botCore, ILogger<BotHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await botCore.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "core failed to start :(");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await botCore.StopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "core failed to stop :(");
        }
    }
}