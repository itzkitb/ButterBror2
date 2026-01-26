// ButterBror.Host/BotHostedService.cs
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ButterBror.Host;

public class BotHostedService : IHostedService
{
    private readonly IBotCore _botCore;
    private readonly ILogger<BotHostedService> _logger;

    public BotHostedService(IBotCore botCore, ILogger<BotHostedService> logger)
    {
        _botCore = botCore;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting bot hosted service...");
        await _botCore.StartAsync(cancellationToken);
        _logger.LogInformation("Bot hosted service started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping bot hosted service...");
        await _botCore.StopAsync(cancellationToken);
        _logger.LogInformation("Bot hosted service stopped successfully");
    }
}