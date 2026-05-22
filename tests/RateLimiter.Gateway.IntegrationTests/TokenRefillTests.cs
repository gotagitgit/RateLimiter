using System.Net;
using FluentAssertions;
using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests;

[Collection("RateLimiter")]
public class TokenRefillTests : IAsyncLifetime
{
    private readonly RateLimiterFixture _fixture;

    public TokenRefillTests(RateLimiterFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _fixture.FlushRedisAsync();

    [Fact]
    public async Task AfterExhausting_WaitOneSecond_NextRequestReturnsOk()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"refill-1s-{Guid.NewGuid()}");

        // Exhaust all tokens
        for (int i = 0; i < RateLimiterFixture.TestBucketCapacity; i++)
        {
            await client.GetAsync("/");
        }

        // Wait for 1 token to refill (slight buffer for timing)
        await Task.Delay(TimeSpan.FromSeconds(1.1));

        var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 1 token refilled, 1 consumed → remaining = 0
        response.Headers.Should().ContainKey("X-Rate-Limit-Remaining");
        var remaining = int.Parse(response.Headers.GetValues("X-Rate-Limit-Remaining").Single());
        remaining.Should().Be(0);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AfterExhausting_WaitNSeconds_RemainingEqualsFloorNTimesRefillRateMinusOne(int waitSeconds)
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"refill-{waitSeconds}s-{Guid.NewGuid()}");

        // Exhaust all tokens
        for (int i = 0; i < RateLimiterFixture.TestBucketCapacity; i++)
        {
            await client.GetAsync("/");
        }

        // Wait N seconds (with slight buffer for timing)
        await Task.Delay(TimeSpan.FromSeconds(waitSeconds + 0.1));

        var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // floor(N * refillRate) tokens refilled, 1 consumed by this request
        var expectedRemaining = (int)Math.Floor(waitSeconds * RateLimiterFixture.TestRefillRate) - 1;

        response.Headers.Should().ContainKey("X-Rate-Limit-Remaining");
        var remaining = int.Parse(response.Headers.GetValues("X-Rate-Limit-Remaining").Single());
        remaining.Should().Be(expectedRemaining);
    }

    [Fact]
    public async Task AfterExhausting_WaitFullRefillDuration_RemainingEqualsCapacityMinusOne()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"refill-full-{Guid.NewGuid()}");

        // Exhaust all tokens
        for (int i = 0; i < RateLimiterFixture.TestBucketCapacity; i++)
        {
            await client.GetAsync("/");
        }

        // Wait long enough for full refill (capacity / refillRate = 5 seconds, add buffer)
        await Task.Delay(TimeSpan.FromSeconds(5.2));

        var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Bucket fully refilled to capacity, 1 consumed → remaining = capacity - 1
        var expectedRemaining = RateLimiterFixture.TestBucketCapacity - 1;

        response.Headers.Should().ContainKey("X-Rate-Limit-Remaining");
        var remaining = int.Parse(response.Headers.GetValues("X-Rate-Limit-Remaining").Single());
        remaining.Should().Be(expectedRemaining);
    }
}
