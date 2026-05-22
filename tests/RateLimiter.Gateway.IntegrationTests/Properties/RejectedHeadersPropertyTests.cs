// Feature: rate-limiter-integration-tests, Property 3: Rejected response header invariants

using System.Net;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests.Properties;

/// <summary>
/// Property 3: Rejected response header invariants
///
/// For any rejected request (HTTP 429), the response SHALL have X-Rate-Limit-Remaining equal to 0,
/// X-Rate-Limit-Limit equal to the configured bucket capacity, and Retry-After as an integer in the
/// range [1, ceiling(capacity / refill_rate)].
///
/// **Validates: Requirements 3.2, 3.3, 3.4**
/// </summary>
[Collection("RateLimiter")]
public class RejectedHeadersPropertyTests
{
    private readonly RateLimiterFixture _fixture;

    public RejectedHeadersPropertyTests(RateLimiterFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 100)]
    public Property RejectedResponses_HaveCorrectHeaders()
    {
        var gen = from extraRequests in Gen.Choose(1, 3) select extraRequests;

        return Prop.ForAll(gen.ToArbitrary(), extraRequests =>
        {
            Task.Run(async () =>
            {
                await _fixture.FlushRedisAsync();
                var client = _fixture.CreateClient();
                client.DefaultRequestHeaders.Add("X-Api-Key", $"prop-rejected-{Guid.NewGuid()}");

                // Exhaust bucket
                for (int i = 0; i < RateLimiterFixture.TestBucketCapacity; i++)
                {
                    await client.GetAsync("/");
                }

                // Verify rejected responses
                for (int i = 0; i < extraRequests; i++)
                {
                    var response = await client.GetAsync("/");

                    response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

                    // Req 3.2: X-Rate-Limit-Remaining = 0
                    response.Headers.Should().ContainKey("X-Rate-Limit-Remaining");
                    var remaining = int.Parse(
                        response.Headers.GetValues("X-Rate-Limit-Remaining").Single());
                    remaining.Should().Be(0);

                    // Req 3.3: X-Rate-Limit-Limit = configured bucket capacity
                    response.Headers.Should().ContainKey("X-Rate-Limit-Limit");
                    var limit = int.Parse(
                        response.Headers.GetValues("X-Rate-Limit-Limit").Single());
                    limit.Should().Be(RateLimiterFixture.TestBucketCapacity);

                    // Req 3.4: Retry-After is an integer in [1, 5]
                    response.Headers.Should().ContainKey("Retry-After");
                    var retryAfter = int.Parse(
                        response.Headers.GetValues("Retry-After").Single());
                    retryAfter.Should().BeGreaterThanOrEqualTo(1);
                    retryAfter.Should().BeLessThanOrEqualTo(RateLimiterFixture.TestBucketCapacity);
                }
            }).GetAwaiter().GetResult();
        });
    }
}
