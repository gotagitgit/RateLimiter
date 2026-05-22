using RateLimiter.Gateway.IntegrationTests.Fixtures;
using Xunit;

namespace RateLimiter.Gateway.IntegrationTests.Collections;

[CollectionDefinition("RateLimiter")]
public class RateLimiterCollection : ICollectionFixture<RateLimiterFixture>
{
}
