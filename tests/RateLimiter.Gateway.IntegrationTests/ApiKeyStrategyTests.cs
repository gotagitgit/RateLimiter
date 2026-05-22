using System.Net;
using FluentAssertions;
using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests;

[Collection("RateLimiter")]
public class ApiKeyStrategyTests : IAsyncLifetime
{
    private readonly RateLimiterFixture _fixture;

    public ApiKeyStrategyTests(RateLimiterFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _fixture.FlushRedisAsync();

    [Fact]
    public async Task RequestWithApiKey_ReturnsLimitMatchingApiKeyRule()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", $"apikey-limit-{Guid.NewGuid()}");

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Rate-Limit-Limit");
        response.Headers.GetValues("X-Rate-Limit-Limit").Single()
            .Should().Be(RateLimiterFixture.TestBucketCapacity.ToString());
    }

    [Fact]
    public async Task TwoDifferentKeys_HaveIndependentBuckets()
    {
        var clientA = _fixture.CreateClient();
        clientA.DefaultRequestHeaders.Add("X-Api-Key", $"apikey-A-{Guid.NewGuid()}");

        var clientB = _fixture.CreateClient();
        clientB.DefaultRequestHeaders.Add("X-Api-Key", $"apikey-B-{Guid.NewGuid()}");

        for (int i = 0; i < 3; i++)
        {
            await clientA.GetAsync("/");
        }

        var responseB = await clientB.GetAsync("/");

        responseB.StatusCode.Should().Be(HttpStatusCode.OK);
        responseB.Headers.GetValues("X-Rate-Limit-Remaining").Single()
            .Should().Be((RateLimiterFixture.TestBucketCapacity - 1).ToString());
    }

    [Fact]
    public async Task ExhaustedKeyGets429_WhileOtherKeyGets200()
    {
        var exhaustedClient = _fixture.CreateClient();
        exhaustedClient.DefaultRequestHeaders.Add("X-Api-Key", $"apikey-exhausted-{Guid.NewGuid()}");

        var freshClient = _fixture.CreateClient();
        freshClient.DefaultRequestHeaders.Add("X-Api-Key", $"apikey-fresh-{Guid.NewGuid()}");

        for (int i = 0; i < RateLimiterFixture.TestBucketCapacity; i++)
        {
            await exhaustedClient.GetAsync("/");
        }

        var exhaustedResponse = await exhaustedClient.GetAsync("/");
        exhaustedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var freshResponse = await freshClient.GetAsync("/");
        freshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EmptyOrWhitespace_FallsThroughToNextStrategy()
    {
        var client = _fixture.CreateClientWithIp("10.99.99.1");
        client.DefaultRequestHeaders.Add("X-Api-Key", "   ");

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Rate-Limit-Limit");
        response.Headers.GetValues("X-Rate-Limit-Limit").Single()
            .Should().Be(RateLimiterFixture.TestBucketCapacity.ToString());

        for (int i = 1; i < RateLimiterFixture.TestBucketCapacity; i++)
        {
            await client.GetAsync("/");
        }

        var exhaustedResponse = await client.GetAsync("/");
        exhaustedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var otherClient = _fixture.CreateClientWithIp("10.99.99.2");
        otherClient.DefaultRequestHeaders.Add("X-Api-Key", "   ");
        var otherResponse = await otherClient.GetAsync("/");
        otherResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
