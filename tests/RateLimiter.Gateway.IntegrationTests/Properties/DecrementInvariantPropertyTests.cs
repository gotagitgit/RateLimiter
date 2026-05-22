// Feature: rate-limiter-integration-tests, Property 1: Sequential request decrement invariant

using System.Net;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests.Properties;

/// <summary>
/// Property 1: Sequential request decrement invariant
///
/// For any number N of sequential requests (where 1 ≤ N ≤ bucket capacity) sent by the same client
/// against a full token bucket, the Nth response SHALL have status 200 and X-Rate-Limit-Remaining
/// equal to (capacity - N).
///
/// **Validates: Requirements 2.4**
/// </summary>
[Collection("RateLimiter")]
public class DecrementInvariantPropertyTests
{
    private readonly RateLimiterFixture _fixture;

    public DecrementInvariantPropertyTests(RateLimiterFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 100)]
    public Property SequentialRequests_DecrementRemaining_Correctly()
    {
        return Prop.ForAll(
            Gen.Choose(1, 5).ToArbitrary(),
            n =>
            {
                // Run async test synchronously within property
                Task.Run(async () =>
                {
                    await _fixture.FlushRedisAsync();
                    var client = _fixture.CreateClient();
                    client.DefaultRequestHeaders.Add("X-Api-Key", $"prop-decrement-{Guid.NewGuid()}");

                    HttpResponseMessage? lastResponse = null;
                    for (int i = 0; i < n; i++)
                    {
                        lastResponse = await client.GetAsync("/");
                    }

                    lastResponse!.StatusCode.Should().Be(HttpStatusCode.OK);
                    var remaining = int.Parse(lastResponse.Headers.GetValues("X-Rate-Limit-Remaining").Single());
                    remaining.Should().Be(RateLimiterFixture.TestBucketCapacity - n);
                }).GetAwaiter().GetResult();
            });
    }
}
