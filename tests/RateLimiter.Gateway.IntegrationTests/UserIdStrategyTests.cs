using System.Net;
using FluentAssertions;
using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests;

[Collection("RateLimiter")]
public class UserIdStrategyTests : IAsyncLifetime
{
    private readonly RateLimiterFixture _fixture;

    public UserIdStrategyTests(RateLimiterFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _fixture.FlushRedisAsync();

    [Fact]
    public async Task RequestWithUserId_ReturnsLimitMatchingUserIdRule()
    {
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", $"user-limit-{Guid.NewGuid()}");

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Rate-Limit-Limit");
        response.Headers.GetValues("X-Rate-Limit-Limit").Single()
            .Should().Be(RateLimiterFixture.TestBucketCapacity.ToString());
    }

    [Fact]
    public async Task TwoDifferentUserIds_HaveIndependentBuckets()
    {
        var clientA = _fixture.CreateClient();
        clientA.DefaultRequestHeaders.Add("X-User-Id", $"user-A-{Guid.NewGuid()}");

        var clientB = _fixture.CreateClient();
        clientB.DefaultRequestHeaders.Add("X-User-Id", $"user-B-{Guid.NewGuid()}");

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
    public async Task RequestWithBothApiKeyAndUserId_UsesApiKeyStrategy()
    {
        var apiKey = $"apikey-priority-{Guid.NewGuid()}";
        var userId = $"user-priority-{Guid.NewGuid()}";

        var dualClient = _fixture.CreateClient();
        dualClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        dualClient.DefaultRequestHeaders.Add("X-User-Id", userId);

        for (int i = 0; i < RateLimiterFixture.TestBucketCapacity; i++)
        {
            await dualClient.GetAsync("/");
        }

        var exhaustedResponse = await dualClient.GetAsync("/");
        exhaustedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var userIdClient = _fixture.CreateClient();
        userIdClient.DefaultRequestHeaders.Add("X-User-Id", userId);

        var userIdResponse = await userIdClient.GetAsync("/");
        userIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        userIdResponse.Headers.GetValues("X-Rate-Limit-Remaining").Single()
            .Should().Be((RateLimiterFixture.TestBucketCapacity - 1).ToString());
    }

    [Fact]
    public async Task EmptyOrWhitespace_FallsThroughToIpAddressStrategy()
    {
        var client = _fixture.CreateClientWithIp("10.88.88.1");
        client.DefaultRequestHeaders.Add("X-User-Id", "   ");

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

        var otherClient = _fixture.CreateClientWithIp("10.88.88.2");
        otherClient.DefaultRequestHeaders.Add("X-User-Id", "   ");
        var otherResponse = await otherClient.GetAsync("/");
        otherResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
