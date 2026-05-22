using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using RateLimiter.Application;
using RateLimiter.Infrastructure.Configuration;
using RateLimiter.Infrastructure.Redis;
using RateLimiter.Infrastructure.Settings;
using StackExchange.Redis;

namespace RateLimiter.Infrastructure;

public static class DependencyInjection
{
    private const string RedisPipelineName = "redis";

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IConfigurationStore, JsonFileConfigurationStore>();
        services.AddHostedService<ConfigurationPoller>();

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var config = configuration.GetValue<string>("RateLimit:Redis:ConnectionString") ?? "localhost:6379";
            return ConnectionMultiplexer.Connect(config);
        });

        SetupRedisResilience(services);

        SetupStateStore(services);

        SetupConfigurations(services, configuration);

        return services;
    }

    private static void SetupConfigurations(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection("RateLimit"));
        services.Configure<ConfigurationPollerOptions>(configuration.GetSection("RateLimit:ConfigPoller"));
        services.Configure<JsonFileConfigurationStoreOptions>(configuration.GetSection("RateLimit:ConfigStore"));
    }

    private static void SetupStateStore(IServiceCollection services)
    {
        services.AddSingleton<IStateStore, RedisStateStore>();
    }

    private static void SetupRedisResilience(IServiceCollection services)
    {
        services.AddResiliencePipeline(RedisPipelineName, (pipelineBuilder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<RateLimitOptions>>().Value;
            pipelineBuilder
                .AddTimeout(TimeSpan.FromMilliseconds(options.TimeoutMs))
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(10),
                    MinimumThroughput = 20,
                    BreakDuration = TimeSpan.FromSeconds(options.HealthCheckIntervalSeconds)
                });
        });
    }
}
