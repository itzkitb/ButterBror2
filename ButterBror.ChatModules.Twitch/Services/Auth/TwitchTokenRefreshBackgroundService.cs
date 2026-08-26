using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ButterBror.ChatModules.Twitch.Services.Auth;

public sealed class TwitchTokenRefreshBackgroundService(
    ITwitchTokenManager tokenManager,
    ILogger<TwitchTokenRefreshBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await tokenManager.RefreshAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "[tw:trbs] token refresh failed; chat transports remain available");
            }
        }
    }
}
