// Feature: rate-limiter-integration-tests, Property 4: Client bucket isolation

using System.Net;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests.Properties;

/// <summary>
/// Property 4: Client bucket isolation
///
/// For any two distinct client identifiers using the same identification strategy,
/// consuming tokens from one client's bucket SHALL NOT reduce the remaining tokens
/// reported for the other client.
///
/// **Validates: Requirements 5.2, 6.2, 7.2**
/// </summary>
[Collection("RateLimiter")]
public class BucketIsolationPropertyTests
{
    private readonly RateLimiterFixture _fixture;

    public BucketIsolationPropertyTests(RateLimiterFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 100)]
    public Property DistinctClients_HaveIsolatedBuckets()
    {
        var gen = from tokensToConsume in Gen.Choose(1, 5)
                  from strategy in Gen.Elements("ApiKey", "UserId", "IpAddress")
                  select (tokensToConsume, strategy);

        return Prop.ForAll(gen.ToArbitrary(), input =>
        {
            Task.Run(async () =>
            {
                await _fixture.FlushRedisAsync();
                var (tokensToConsume, strategy) = input;

                var idA = Guid.NewGuid().ToString();
                var idB = Guid.NewGuid().ToString();

                HttpClient clientA, clientB;
                switch (strategy)
                {
                    case "ApiKey":
                        clientA = _fixture.CreateClient();
                        clientA.DefaultRequestHeaders.Add("X-Api-Key", idA);
                        clientB = _fixture.CreateClient();
                        clientB.DefaultRequestHeaders.Add("X-Api-Key", idB);
                        break;
                    case "UserId":
                        clientA = _fixture.CreateClient();
                        clientA.DefaultRequestHeaders.Add("X-User-Id", idA);
                        clientB = _fixture.CreateClient();
                        clientB.DefaultRequestHeaders.Add("X-User-Id", idB);
                        break;
                    default: // IpAddress
                        clientA = _fixture.CreateClientWithIp($"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.1");
                        clientB = _fixture.CreateClientWithIp($"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.2");
                        break;
                }

                // Consume tokens from client A
                for (int i = 0; i < tokensToConsume; i++)
                {
                    await clientA.GetAsync("/");
                }

                // Client B should be unaffected
                var responseB = await clientB.GetAsync("/");
                responseB.StatusCode.Should().Be(HttpStatusCode.OK);
                var remaining = int.Parse(responseB.Headers.GetValues("X-Rate-Limit-Remaining").Single());
                remaining.Should().Be(RateLimiterFixture.TestBucketCapacity - 1);
            }).GetAwaiter().GetResult();
        });
    }
}
