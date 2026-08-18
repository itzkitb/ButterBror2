using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ButterBror.Host;

public class BotHostedService(IBotCore botCore, ILogger<BotHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await botCore.StartAsync(cancellationToken);
        logger.LogInformation("the bot host has started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await botCore.StopAsync(cancellationToken);
        logger.LogInformation("bot host stopped successfully");
    }
}