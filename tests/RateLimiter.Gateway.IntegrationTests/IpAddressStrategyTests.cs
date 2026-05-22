using System.Net;
using FluentAssertions;
using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests;

[Collection("RateLimiter")]
public class IpAddressStrategyTests : IAsyncLifetime
{
    private readonly RateLimiterFixture _fixture;

    public IpAddressStrategyTests(RateLimiterFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _fixture.FlushRedisAsync();

    [Fact]
    public async Task RequestWithoutIdentificationHeaders_AppliesIpAddressRule()
    {
        var client = _fixture.CreateClientWithIp("10.0.1.1");

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Rate-Limit-Limit");
        response.Headers.GetValues("X-Rate-Limit-Limit").Single()
            .Should().Be(RateLimiterFixture.TestBucketCapacity.ToString());
    }

    [Fact]
    public async Task TwoDifferentIps_HaveIndependentBuckets()
    {
        var clientA = _fixture.CreateClientWithIp("10.0.2.1");
        var clientB = _fixture.CreateClientWithIp("10.0.2.2");

        for (int i = 0; i < RateLimiterFixture.TestBucketCapacity; i++)
        {
            await clientA.GetAsync("/");
        }

        var exhaustedResponse = await clientA.GetAsync("/");
        exhaustedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var responseB = await clientB.GetAsync("/");
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);
        responseB.Headers.GetValues("X-Rate-Limit-Remaining").Single()
            .Should().Be((RateLimiterFixture.TestBucketCapacity - 1).ToString());
    }

    [Fact]
    public async Task DistinctRemoteIpsViaHeader_VerifiesIsolation()
    {
        var clientX = _fixture.CreateClientWithIp("10.0.3.1");
        var clientY = _fixture.CreateClientWithIp("10.0.3.2");

        for (int i = 0; i < 4; i++)
        {
            await clientX.GetAsync("/");
        }

        var lastResponseX = await clientX.GetAsync("/");
        lastResponseX.StatusCode.Should().Be(HttpStatusCode.OK);
        lastResponseX.Headers.GetValues("X-Rate-Limit-Remaining").Single()
            .Should().Be("0");

        var responseY = await clientY.GetAsync("/");
        responseY.StatusCode.Should().Be(HttpStatusCode.OK);
        responseY.Headers.GetValues("X-Rate-Limit-Remaining").Single()
            .Should().Be((RateLimiterFixture.TestBucketCapacity - 1).ToString());

        var exhaustedX = await clientX.GetAsync("/");
        exhaustedX.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var secondResponseY = await clientY.GetAsync("/");
        secondResponseY.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
