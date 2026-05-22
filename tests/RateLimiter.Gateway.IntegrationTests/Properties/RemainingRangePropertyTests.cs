// Feature: rate-limiter-integration-tests, Property 2: Remaining header range invariant

using System.Net;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests.Properties;

/// <summary>
/// Property 2: Remaining header range invariant
///
/// For any allowed request (HTTP 200), the X-Rate-Limit-Remaining header value
/// SHALL be a non-negative integer in the range [0, bucket capacity].
///
/// **Validates: Requirements 2.5**
/// </summary>
[Collection("RateLimiter")]
public class RemainingRangePropertyTests
{
    private readonly RateLimiterFixture _fixture;

    public RemainingRangePropertyTests(RateLimiterFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 100)]
    public Property AllowedResponses_HaveRemainingInValidRange()
    {
        var gen =
            from n in Gen.Choose(1, 5)
            select n;

        return Prop.ForAll(gen.ToArbitrary(), n =>
        {
            Task.Run(async () =>
            {
                await _fixture.FlushRedisAsync();
                var client = _fixture.CreateClient();
                client.DefaultRequestHeaders.Add("X-Api-Key", $"prop-range-{Guid.NewGuid()}");

                for (int i = 0; i < n; i++)
                {
                    var response = await client.GetAsync("/");
                    response.StatusCode.Should().Be(HttpStatusCode.OK);
                    var remaining = int.Parse(response.Headers.GetValues("X-Rate-Limit-Remaining").Single());
                    remaining.Should().BeGreaterThanOrEqualTo(0);
                    remaining.Should().BeLessThanOrEqualTo(RateLimiterFixture.TestBucketCapacity);
                }
            }).GetAwaiter().GetResult();
        });
    }
}
