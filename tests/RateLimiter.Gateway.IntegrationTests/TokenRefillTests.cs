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

        // Wait for at least 1 token to refill (generous buffer for CI/timing jitter)
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // At least 1 token refilled and 1 consumed → remaining is 0 or slightly more
        response.Headers.Should().ContainKey("X-Rate-Limit-Remaining");
        var remaining = int.Parse(response.Headers.GetValues("X-Rate-Limit-Remaining").Single());
        remaining.Should().BeGreaterThanOrEqualTo(0);
        remaining.Should().BeLessThan(RateLimiterFixture.TestBucketCapacity);
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

        // Wait N seconds (generous buffer for CI/timing jitter)
        await Task.Delay(TimeSpan.FromSeconds(waitSeconds + 0.5));

        var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // floor(N * refillRate) tokens refilled, 1 consumed by this request.
        // Allow ±1 tolerance for timing jitter between Task.Delay and Redis TIME.
        var expectedRemaining = (int)Math.Floor(waitSeconds * RateLimiterFixture.TestRefillRate) - 1;

        response.Headers.Should().ContainKey("X-Rate-Limit-Remaining");
        var remaining = int.Parse(response.Headers.GetValues("X-Rate-Limit-Remaining").Single());
        remaining.Should().BeGreaterThanOrEqualTo(expectedRemaining - 1,
            because: $"after ~{waitSeconds}s at refill rate {RateLimiterFixture.TestRefillRate}/s, " +
                     $"remaining should be approximately {expectedRemaining} (timing tolerance ±1)");
        remaining.Should().BeLessThanOrEqualTo(expectedRemaining + 1);
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

        // Wait long enough for full refill (capacity / refillRate = 5 seconds, generous buffer)
        await Task.Delay(TimeSpan.FromSeconds(6.0));

        var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Bucket fully refilled to capacity, 1 consumed → remaining = capacity - 1.
        // Allow -1 tolerance in case timing jitter means we're slightly short of full refill.
        var expectedRemaining = RateLimiterFixture.TestBucketCapacity - 1;

        response.Headers.Should().ContainKey("X-Rate-Limit-Remaining");
        var remaining = int.Parse(response.Headers.GetValues("X-Rate-Limit-Remaining").Single());
        remaining.Should().BeGreaterThanOrEqualTo(expectedRemaining - 1,
            because: "bucket should be at or near full capacity after waiting for full refill duration");
        remaining.Should().BeLessThanOrEqualTo(expectedRemaining);
    }
}
