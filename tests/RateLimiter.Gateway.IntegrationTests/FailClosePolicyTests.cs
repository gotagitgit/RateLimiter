using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests;

[Collection("RateLimiter")]
public class FailClosePolicyTests : IAsyncLifetime
{
    private readonly RateLimiterFixture _fixture;
    private WebApplicationFactory<Program> _failCloseFactory = null!;
    private string _connectionString = null!;

    public FailClosePolicyTests(RateLimiterFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        // Use a non-routable Redis endpoint with abortConnect=false so the
        // ConnectionMultiplexer can be created without an active connection.
        // When the middleware tries to execute Redis commands, it will throw
        // and the FailClose policy will be applied.
        _connectionString = "localhost:1,abortConnect=false,connectTimeout=1000,syncTimeout=1000,asyncTimeout=1000";

        // Create a dedicated factory pointing to an unreachable Redis
        _failCloseFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimit:Redis:ConnectionString"] = _connectionString,
                    ["RateLimit:FailurePolicy"] = "FailClose",
                    ["RateLimit:TimeoutMs"] = "1000",
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

    public async Task DisposeAsync()
    {
        _failCloseFactory?.Dispose();
        await _fixture.FlushRedisAsync();
    }

    [Fact]
    public async Task WhenRedisUnavailable_RequestReturns429()
    {
        var client = _failCloseFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"failclose-{Guid.NewGuid()}");

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task WhenRedisUnavailable_ThreeSequentialRequestsAllReturn429()
    {
        var client = _failCloseFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"failclose-seq-{Guid.NewGuid()}");

        for (int i = 0; i < 3; i++)
        {
            var response = await client.GetAsync("/");
            response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
                because: $"request {i + 1} should return 429 when Redis is unavailable");
        }
    }

    [Fact]
    public async Task WhenRedisUnavailable_ResponseDoesNotIncludeRateLimitHeaders()
    {
        var client = _failCloseFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"failclose-headers-{Guid.NewGuid()}");

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.GetValues("X-Rate-Limit-Remaining").Single().Should().Be("0");
        response.Headers.GetValues("X-Rate-Limit-Limit").Single().Should().Be(
            RateLimiterFixture.TestBucketCapacity.ToString());
        response.Headers.Should().ContainKey("Retry-After");
    }

    [Fact]
    public async Task WhenRedisUnavailable_ResponseBodyIsEmpty()
    {
        var client = _failCloseFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"failclose-body-{Guid.NewGuid()}");

        var response = await client.GetAsync("/");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().BeEmpty();
    }

    /// <summary>
    /// Startup filter that inserts the IP-override middleware at the beginning of the pipeline.
    /// </summary>
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
