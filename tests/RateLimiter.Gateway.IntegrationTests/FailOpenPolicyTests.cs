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
public class FailOpenPolicyTests : IAsyncLifetime
{
    private readonly RateLimiterFixture _fixture;
    private WebApplicationFactory<Program>? _failOpenFactory;

    public FailOpenPolicyTests(RateLimiterFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        // Stop Redis to simulate unavailability
        await _fixture.StopRedisAsync();

        // Create a separate factory configured with FailOpen policy and an unreachable Redis
        _failOpenFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimit:Redis:ConnectionString"] = "localhost:59999,connectTimeout=1000,syncTimeout=1000,abortConnect=false",
                    ["RateLimit:FailurePolicy"] = "FailOpen",
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
        _failOpenFactory?.Dispose();

        // Restart Redis for other test classes
        await _fixture.StartRedisAsync();
        await _fixture.FlushRedisAsync();
    }

    /// <summary>
    /// Verifies that when Redis is unavailable and FailurePolicy is FailOpen,
    /// a request returns HTTP 200.
    /// </summary>
    [Fact]
    public async Task Request_ReturnsHttp200_WhenRedisUnavailableAndFailOpen()
    {
        // Arrange
        using var client = _failOpenFactory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "failopen-test-key-001");

        // Act
        var response = await client.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that when Redis is unavailable and FailurePolicy is FailOpen,
    /// the response does not include rate limit headers.
    /// </summary>
    [Fact]
    public async Task Response_DoesNotIncludeRateLimitHeaders_WhenRedisUnavailableAndFailOpen()
    {
        // Arrange
        using var client = _failOpenFactory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "failopen-test-key-002");

        // Act
        var response = await client.GetAsync("/");

        // Assert
        response.Headers.Should().NotContain(h => h.Key == "X-Rate-Limit-Limit");
        response.Headers.Should().NotContain(h => h.Key == "X-Rate-Limit-Remaining");
        response.Headers.Should().NotContain(h => h.Key == "Retry-After");
    }

    /// <summary>
    /// Verifies that when Redis is unavailable and FailurePolicy is FailOpen,
    /// multiple sequential requests all return HTTP 200.
    /// </summary>
    [Fact]
    public async Task MultipleRequests_AllReturnHttp200_WhenRedisUnavailableAndFailOpen()
    {
        // Arrange
        using var client = _failOpenFactory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "failopen-test-key-003");

        // Act & Assert
        for (var i = 0; i < 3; i++)
        {
            var response = await client.GetAsync("/");
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                because: $"request {i + 1} should succeed when Redis is unavailable with FailOpen policy");
        }
    }

    /// <summary>
    /// Startup filter that inserts the IP-override middleware at the beginning of the pipeline.
    /// This ensures the X-Test-Remote-Ip header is processed before the rate limit middleware.
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
