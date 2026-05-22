using System.Net;
using FluentAssertions;
using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests;

[Collection("RateLimiter")]
public class IdentificationPriorityTests : IAsyncLifetime
{
    private readonly RateLimiterFixture _fixture;

    public IdentificationPriorityTests(RateLimiterFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _fixture.FlushRedisAsync();

    [Fact]
    public async Task ApiKeyAndUserId_ApiKeyRuleApplied_ConfirmedByLimitHeader()
    {
        var client = _fixture.CreateClient();
        var apiKey = $"priority-apikey-{Guid.NewGuid()}";
        var userId = $"priority-userid-{Guid.NewGuid()}";
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        client.DefaultRequestHeaders.Add("X-User-Id", userId);

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Rate-Limit-Limit");
        response.Headers.GetValues("X-Rate-Limit-Limit").Single()
            .Should().Be(RateLimiterFixture.TestBucketCapacity.ToString());
    }

    [Fact]
    public async Task UserIdOnly_UserIdRuleApplied_NotIpAddressRule()
    {
        var client = _fixture.CreateClient();
        var userId = $"priority-userid-only-{Guid.NewGuid()}";
        client.DefaultRequestHeaders.Add("X-User-Id", userId);

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Rate-Limit-Limit");
        response.Headers.GetValues("X-Rate-Limit-Limit").Single()
            .Should().Be(RateLimiterFixture.TestBucketCapacity.ToString());
    }

    [Fact]
    public async Task ExhaustApiKeyBucket_DoesNotDecrementUserIdBucket()
    {
        var apiKey = $"priority-exhaust-apikey-{Guid.NewGuid()}";
        var userId = $"priority-exhaust-userid-{Guid.NewGuid()}";

        // Exhaust the ApiKey bucket by sending capacity requests with both headers
        var apiKeyClient = _fixture.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        apiKeyClient.DefaultRequestHeaders.Add("X-User-Id", userId);

        for (int i = 0; i < RateLimiterFixture.TestBucketCapacity; i++)
        {
            var exhaustResponse = await apiKeyClient.GetAsync("/");
            exhaustResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Verify ApiKey bucket is actually exhausted
        var rejectedResponse = await apiKeyClient.GetAsync("/");
        rejectedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Now send a request with only the UserId header (same user ID)
        // If buckets are isolated, the UserId bucket should be untouched
        var userIdClient = _fixture.CreateClient();
        userIdClient.DefaultRequestHeaders.Add("X-User-Id", userId);

        var userIdResponse = await userIdClient.GetAsync("/");

        userIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        userIdResponse.Headers.Should().ContainKey("X-Rate-Limit-Remaining");
        userIdResponse.Headers.GetValues("X-Rate-Limit-Remaining").Single()
            .Should().Be((RateLimiterFixture.TestBucketCapacity - 1).ToString());
    }
}
