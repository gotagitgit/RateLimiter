using System.Net;
using FluentAssertions;
using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests;

[Collection("RateLimiter")]
public class InfrastructureTests : IAsyncLifetime
{
    private readonly RateLimiterFixture _fixture;

    public InfrastructureTests(RateLimiterFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _fixture.FlushRedisAsync();

    /// <summary>
    /// Verifies that the fixture starts successfully and Redis responds to PING.
    /// </summary>
    [Fact]
    public async Task Fixture_StartsSuccessfully_RedisRespondsToPing()
    {
        // Arrange
        var db = _fixture.GetRedisDatabase();

        // Act
        var pingResult = await db.PingAsync();

        // Assert
        _fixture.Should().NotBeNull();
        pingResult.Should().BeGreaterThan(TimeSpan.Zero);
    }

    /// <summary>
    /// Verifies that FLUSHDB clears all keys between tests.
    /// </summary>
    [Fact]
    public async Task FlushRedis_ClearsAllKeys_EnsuresTestIsolation()
    {
        // Arrange
        var db = _fixture.GetRedisDatabase();
        var testKey = "infrastructure-test:flush-verification";
        await db.StringSetAsync(testKey, "test-value");

        // Verify the key exists before flush
        var existsBefore = await db.KeyExistsAsync(testKey);
        existsBefore.Should().BeTrue();

        // Act
        await _fixture.FlushRedisAsync();

        // Assert
        var existsAfter = await db.KeyExistsAsync(testKey);
        existsAfter.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that WebApplicationFactory hosts the application with overridden config.
    /// </summary>
    [Fact]
    public async Task WebApplicationFactory_HostsApplication_WithOverriddenConfig()
    {
        // Arrange
        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "infra-test-key-001");

        // Act
        var response = await client.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("Hello World!");
    }
}
