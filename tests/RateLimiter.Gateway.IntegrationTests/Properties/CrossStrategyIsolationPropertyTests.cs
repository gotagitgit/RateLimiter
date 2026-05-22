// Feature: rate-limiter-integration-tests, Property 5: Cross-strategy bucket isolation

using System.Net;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests.Properties;

/// <summary>
/// Property 5: Cross-strategy bucket isolation
///
/// For any client that presents both an X-Api-Key and X-User-Id header, exhausting the
/// ApiKey-strategy bucket SHALL NOT decrement the UserId-strategy bucket, verified by a
/// subsequent request using only the UserId header returning X-Rate-Limit-Remaining equal
/// to (capacity - 1).
///
/// **Validates: Requirements 8.3**
/// </summary>
[Collection("RateLimiter")]
public class CrossStrategyIsolationPropertyTests
{
    private readonly RateLimiterFixture _fixture;

    public CrossStrategyIsolationPropertyTests(RateLimiterFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 100)]
    public Property ExhaustingApiKeyBucket_DoesNotAffectUserIdBucket()
    {
        var gen = from apiKeySuffix in Gen.Choose(1, int.MaxValue)
                  from userIdSuffix in Gen.Choose(1, int.MaxValue)
                  select (apiKeySuffix, userIdSuffix);

        return Prop.ForAll(gen.ToArbitrary(), input =>
        {
            Task.Run(async () =>
            {
                await _fixture.FlushRedisAsync();
                var apiKey = $"prop-cross-apikey-{Guid.NewGuid()}";
                var userId = $"prop-cross-userid-{Guid.NewGuid()}";

                // Exhaust ApiKey bucket with both headers
                var dualClient = _fixture.CreateClient();
                dualClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
                dualClient.DefaultRequestHeaders.Add("X-User-Id", userId);

                for (int i = 0; i < RateLimiterFixture.TestBucketCapacity; i++)
                {
                    await dualClient.GetAsync("/");
                }

                // Verify ApiKey bucket is exhausted
                var rejectedResponse = await dualClient.GetAsync("/");
                rejectedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

                // Verify UserId bucket is unaffected
                var userIdClient = _fixture.CreateClient();
                userIdClient.DefaultRequestHeaders.Add("X-User-Id", userId);

                var userIdResponse = await userIdClient.GetAsync("/");
                userIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var remaining = int.Parse(userIdResponse.Headers.GetValues("X-Rate-Limit-Remaining").Single());
                remaining.Should().Be(RateLimiterFixture.TestBucketCapacity - 1);
            }).GetAwaiter().GetResult();
        });
    }
}
