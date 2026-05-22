using System.Net;
using FluentAssertions;
using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests;

[Collection("RateLimiter")]
public class RejectedRequestTests : IAsyncLifetime
{
    private readonly RateLimiterFixture _fixture;

    public RejectedRequestTests(RateLimiterFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _fixture.FlushRedisAsync();

    private async Task<HttpResponseMessage> ExhaustBucketAndGetRejectedResponse(HttpClient client)
    {
        for (int i = 0; i < RateLimiterFixture.TestBucketCapacity; i++)
        {
            await client.GetAsync("/");
        }

        return await client.GetAsync("/");
    }

    [Fact]
    public async Task SixthRequest_AfterExhaustingBucket_ReturnsHttp429()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"rejected-429-{Guid.NewGuid()}");

        var response = await ExhaustBucketAndGetRejectedResponse(client);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task RejectedResponse_IncludesRemainingHeaderEqualToZero()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"rejected-remaining-{Guid.NewGuid()}");

        var response = await ExhaustBucketAndGetRejectedResponse(client);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.Should().ContainKey("X-Rate-Limit-Remaining");
        var remainingValue = response.Headers.GetValues("X-Rate-Limit-Remaining").Single();
        remainingValue.Should().Be("0");
    }

    [Fact]
    public async Task RejectedResponse_IncludesLimitHeaderEqualToCapacity()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"rejected-limit-{Guid.NewGuid()}");

        var response = await ExhaustBucketAndGetRejectedResponse(client);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.Should().ContainKey("X-Rate-Limit-Limit");
        var limitValue = response.Headers.GetValues("X-Rate-Limit-Limit").Single();
        limitValue.Should().Be(RateLimiterFixture.TestBucketCapacity.ToString());
    }

    [Fact]
    public async Task RejectedResponse_IncludesRetryAfterHeaderWithIntegerInRange()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"rejected-retry-{Guid.NewGuid()}");

        var response = await ExhaustBucketAndGetRejectedResponse(client);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.Should().ContainKey("Retry-After");
        var retryAfterValue = response.Headers.GetValues("Retry-After").Single();
        var retryAfterInt = int.Parse(retryAfterValue);
        retryAfterInt.Should().BeGreaterThanOrEqualTo(1);
        retryAfterInt.Should().BeLessThanOrEqualTo(RateLimiterFixture.TestBucketCapacity);
    }

    [Fact]
    public async Task AllSubsequentRequests_AfterExhaustion_Return429()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"rejected-subsequent-{Guid.NewGuid()}");

        // Exhaust the bucket
        for (int i = 0; i < RateLimiterFixture.TestBucketCapacity; i++)
        {
            await client.GetAsync("/");
        }

        // Send 3 additional requests — all should be 429
        for (int i = 0; i < 3; i++)
        {
            var response = await client.GetAsync("/");
            response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
                $"request {i + 1} after exhaustion should be 429");
        }
    }

    [Fact]
    public async Task RejectedResponse_BodyHasContentLengthOfZeroBytes()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"rejected-body-{Guid.NewGuid()}");

        var response = await ExhaustBucketAndGetRejectedResponse(client);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var body = await response.Content.ReadAsByteArrayAsync();
        body.Should().BeEmpty();
    }
}
