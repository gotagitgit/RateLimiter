using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Redis;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests;

public sealed class ProxyRateLimitTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder().Build();
    private WireMockServer _backendServer = null!;
    private WebApplicationFactory<Program> _factory = null!;

    private const int TestBucketCapacity = 3;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        _backendServer = WireMockServer.Start();
        _backendServer
            .Given(Request.Create().WithPath("/api/products").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""[{"id":1,"name":"Widget","price":9.99}]"""));

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimit:Redis:ConnectionString"] = _redisContainer.GetConnectionString(),
                    ["RateLimit:FailurePolicy"] = "FailClose",
                    ["RateLimit:TimeoutMs"] = "5000",
                    ["RateLimit:HealthCheckIntervalSeconds"] = "1",
                    ["RateLimit:ConfigStore:FilePath"] = "test-ratelimit-rules.json",
                    ["ReverseProxy:Routes:sampleapi-route:ClusterId"] = "sampleapi-cluster",
                    ["ReverseProxy:Routes:sampleapi-route:Match:Path"] = "/api/{**catch-all}",
                    ["ReverseProxy:Clusters:sampleapi-cluster:Destinations:destination1:Address"] = _backendServer.Url!
                });
            });
        });
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        _backendServer?.Stop();
        _backendServer?.Dispose();
        await _redisContainer.DisposeAsync();
    }

    [Fact]
    public async Task ProxiedRequest_WithinRateLimit_ReturnsBackendResponse()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Widget");
    }

    [Fact]
    public async Task ProxiedRequest_WithinRateLimit_HasRateLimitHeaders()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Rate-Limit-Limit");
        response.Headers.Should().ContainKey("X-Rate-Limit-Remaining");
    }

    [Fact]
    public async Task ProxiedRequest_ExceedsRateLimit_Returns429()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Exhaust the token bucket by sending many requests
        for (int i = 0; i < 200; i++)
        {
            var r = await client.GetAsync("/api/products");
            if (r.StatusCode == HttpStatusCode.TooManyRequests)
                break;
        }

        // Act — this request should be rate-limited
        var response = await client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.Should().ContainKey("Retry-After");
    }

    [Fact]
    public async Task ProxiedRequest_WhenRateLimited_NeverReachesBackend()
    {
        // Arrange
        var client = _factory.CreateClient();
        var initialRequestCount = _backendServer.LogEntries.Count;

        // Exhaust the token bucket
        for (int i = 0; i < 200; i++)
        {
            var r = await client.GetAsync("/api/products");
            if (r.StatusCode == HttpStatusCode.TooManyRequests)
                break;
        }

        var requestCountAfterExhaustion = _backendServer.LogEntries.Count;

        // Act — send another request that should be blocked
        var response = await client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        _backendServer.LogEntries.Count.Should().Be(requestCountAfterExhaustion,
            "rate-limited requests should never reach the backend");
    }
}
