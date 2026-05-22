using System.Net;
using FluentAssertions;
using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests;

[Collection("RateLimiter")]
public class AllowedRequestTests : IAsyncLifetime
{
    private readonly RateLimiterFixture _fixture;

    public AllowedRequestTests(RateLimiterFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _fixture.FlushRedisAsync();

    [Fact]
    public async Task SingleRequest_ReturnsOk()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"allowed-single-{Guid.NewGuid()}");

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AllowedResponse_IncludesRateLimitLimitHeader_EqualToCapacity()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"allowed-limit-{Guid.NewGuid()}");

        var response = await client.GetAsync("/");

        response.Headers.Should().ContainKey("X-Rate-Limit-Limit");
        var limitValue = response.Headers.GetValues("X-Rate-Limit-Limit").Single();
        limitValue.Should().Be(RateLimiterFixture.TestBucketCapacity.ToString());
    }

    [Fact]
    public async Task FirstRequest_ReturnsRemainingEqualToCapacityMinusOne()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"allowed-first-{Guid.NewGuid()}");

        var response = await client.GetAsync("/");

        response.Headers.Should().ContainKey("X-Rate-Limit-Remaining");
        var remainingValue = response.Headers.GetValues("X-Rate-Limit-Remaining").Single();
        remainingValue.Should().Be((RateLimiterFixture.TestBucketCapacity - 1).ToString());
    }

    [Fact]
    public async Task SequentialRequests_ReturnOkWithDecrementingRemaining()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"allowed-seq-{Guid.NewGuid()}");

        for (int i = 1; i <= RateLimiterFixture.TestBucketCapacity; i++)
        {
            var response = await client.GetAsync("/");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.Should().ContainKey("X-Rate-Limit-Remaining");
            var remainingValue = response.Headers.GetValues("X-Rate-Limit-Remaining").Single();
            remainingValue.Should().Be((RateLimiterFixture.TestBucketCapacity - i).ToString());
        }
    }

    [Fact]
    public async Task AllowedResponse_RemainingIsNonNegativeIntegerWithinCapacity()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"allowed-range-{Guid.NewGuid()}");

        for (int i = 1; i <= RateLimiterFixture.TestBucketCapacity; i++)
        {
            var response = await client.GetAsync("/");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.Should().ContainKey("X-Rate-Limit-Remaining");
            var remainingStr = response.Headers.GetValues("X-Rate-Limit-Remaining").Single();
            var remaining = int.Parse(remainingStr);
            remaining.Should().BeGreaterThanOrEqualTo(0);
            remaining.Should().BeLessThanOrEqualTo(RateLimiterFixture.TestBucketCapacity);
        }
    }

    [Fact]
    public async Task AllowedResponse_BodyIsHelloWorldWithTextPlainContentType()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"allowed-body-{Guid.NewGuid()}");

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("Hello World!");

        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
    }
}
