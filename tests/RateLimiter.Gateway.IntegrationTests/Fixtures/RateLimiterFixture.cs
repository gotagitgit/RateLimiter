using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests.Fixtures;

public sealed class RateLimiterFixture : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder().Build();
    private WebApplicationFactory<Program> _factory = null!;
    private IConnectionMultiplexer _redis = null!;

    public const int TestBucketCapacity = 5;

    public const double TestRefillRate = 1.0;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var connectionString = _redisContainer.GetConnectionString();
        var configOptions = ConfigurationOptions.Parse(connectionString);
        configOptions.ConnectTimeout = 30_000;
        configOptions.SyncTimeout = 30_000;
        configOptions.AsyncTimeout = 30_000;
        configOptions.AllowAdmin = true;

        _redis = await ConnectionMultiplexer.ConnectAsync(configOptions);

        var db = _redis.GetDatabase();
        var pingResult = await db.PingAsync();
        if (pingResult == TimeSpan.Zero)
        {
            throw new TimeoutException("Redis PING did not respond within 30 seconds.");
        }

        _factory = CreateFactory();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();

        if (_redis is not null)
        {
            await _redis.CloseAsync();
            _redis.Dispose();
        }

        await _redisContainer.DisposeAsync();
    }

    public HttpClient CreateClient() => _factory.CreateClient();

    public HttpClient CreateClientWithIp(string ipAddress)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Remote-Ip", ipAddress);
        return client;
    }

    public IDatabase GetRedisDatabase() => _redis.GetDatabase();

    public async Task FlushRedisAsync()
    {
        if (_redis is null || !_redis.IsConnected)
            return;

        var server = _redis.GetServer(_redis.GetEndPoints().First());
        await server.FlushDatabaseAsync();
    }

    public async Task StopRedisAsync()
    {
        if (_redis is not null)
        {
            await _redis.CloseAsync();
            _redis.Dispose();
            _redis = null!;
        }

        await _redisContainer.StopAsync();
    }

    public async Task StartRedisAsync()
    {
        await _redisContainer.StartAsync();

        // Reconnect after restart — the container may have a new port mapping
        var connectionString = _redisContainer.GetConnectionString();
        var configOptions = ConfigurationOptions.Parse(connectionString);
        configOptions.ConnectTimeout = 30_000;
        configOptions.SyncTimeout = 30_000;
        configOptions.AsyncTimeout = 30_000;
        configOptions.AllowAdmin = true;

        _redis = await ConnectionMultiplexer.ConnectAsync(configOptions);

        // Recreate the factory so it uses the new Redis connection string
        _factory?.Dispose();
        _factory = CreateFactory();
    }

    private WebApplicationFactory<Program> CreateFactory(string failurePolicy = "FailClose")
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimit:Redis:ConnectionString"] = _redisContainer.GetConnectionString(),
                    ["RateLimit:FailurePolicy"] = failurePolicy,
                    ["RateLimit:TimeoutMs"] = "5000",
                    ["RateLimit:HealthCheckIntervalSeconds"] = "1",
                    ["RateLimit:ConfigStore:FilePath"] = "test-ratelimit-rules.json"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.AddTransient<IStartupFilter, IpOverrideStartupFilter>();
            });
        });
    }

    private sealed class IpOverrideStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (context.Request.Headers.TryGetValue("X-Test-Remote-Ip", out var ip))
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse(ip!);
                    }
                    await nextMiddleware();
                });

                next(app);
            };
        }
    }
}
