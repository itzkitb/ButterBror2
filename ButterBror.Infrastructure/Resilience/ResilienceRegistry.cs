using Microsoft.Extensions.DependencyInjection;
using Polly;
using StackExchange.Redis;

namespace ButterBror.Infrastructure.Resilience;

public static class ResilienceRegistry
{
    public static void RegisterResilienceStrategies(this IServiceCollection services)
    {
        // Redis pipeline
        services.AddResiliencePipeline("redis", builder =>
        {
            builder.AddRetry(new()
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(0.5),
                BackoffType = DelayBackoffType.Exponential,
                MaxDelay = TimeSpan.FromSeconds(2),
                ShouldHandle = new PredicateBuilder()
                    .Handle<RedisConnectionException>()
                    .Handle<RedisTimeoutException>()
                    .Handle<TimeoutException>()
            });

            builder.AddTimeout(TimeSpan.FromSeconds(3));

            builder.AddCircuitBreaker(new()
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(10),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30)
            });
        });

        // Twitch API
        services.AddResiliencePipeline("twitch", builder =>
        {
            builder.AddRetry(new()
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(1),
                ShouldHandle = new PredicateBuilder()
                    .Handle<TimeoutException>()
                    .Handle<HttpRequestException>()
            });

            builder.AddTimeout(TimeSpan.FromSeconds(10));
        });
    }
}