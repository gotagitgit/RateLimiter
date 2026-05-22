// Feature: distributed-rate-limiter, Property 3: Token consumption on allowed requests

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FluentAssertions;
using RateLimiter.Domain;

namespace RateLimiter.Domain.Tests;

/// <summary>
/// Property-based tests for the TokenBucketAlgorithm.
/// </summary>
public class TokenBucketAlgorithmPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 2.2, 3.1**
    ///
    /// Property 3: Token consumption on allowed requests
    /// For any token bucket state where available tokens >= 1, evaluating a rate limit check
    /// SHALL return IsAllowed = true and the resulting available tokens SHALL equal the
    /// previous available tokens minus one.
    /// </summary>
    [Property]
    public Property TokenConsumption_WhenTokensAvailable_ShouldAllowAndDecrementByOne()
    {
        var gen =
            from availableTokens in Gen.Choose(1, 10000).Select(i => (double)i)
            from lastRefillTimestampMs in Gen.Choose(0, 1_000_000).Select(i => (long)i)
            from bucketCapacity in Gen.Choose(1, 10000)
            from refillRateIndex in Gen.Choose(0, 6)
            let refillRates = new[] { 0.1, 0.5, 1.0, 5.0, 10.0, 50.0, 100.0 }
            let refillRate = refillRates[refillRateIndex]
            // Ensure availableTokens does not exceed capacity
            let cappedTokens = Math.Min(availableTokens, (double)bucketCapacity)
            where cappedTokens >= 1.0
            select new
            {
                AvailableTokens = cappedTokens,
                LastRefillTimestampMs = lastRefillTimestampMs,
                BucketCapacity = bucketCapacity,
                RefillRate = refillRate
            };

        return Prop.ForAll(gen.ToArbitrary(), input =>
        {
            var state = new TokenBucketState(input.AvailableTokens, input.LastRefillTimestampMs);
            var rule = new RateLimitRule("test-rule", input.BucketCapacity, input.RefillRate, null);
            // Use zero elapsed time (nowTimestampMs == LastRefillTimestampMs) to isolate consumption
            var nowTimestampMs = input.LastRefillTimestampMs;

            var algorithm = new TokenBucketAlgorithm();
            var result = algorithm.Evaluate(state, rule, nowTimestampMs);

            // The request should be allowed
            result.IsAllowed.Should().BeTrue(
                because: $"available tokens ({input.AvailableTokens}) >= 1");

            // Remaining should be floor(previous tokens - 1)
            var expectedRemaining = (int)Math.Floor(input.AvailableTokens - 1.0);
            result.Remaining.Should().Be(expectedRemaining,
                because: $"tokens were {input.AvailableTokens}, after consuming 1 and flooring: {expectedRemaining}");

            // Limit should reflect the bucket capacity
            result.Limit.Should().Be(input.BucketCapacity);

            // RetryAfterSeconds should be 0 for allowed requests
            result.RetryAfterSeconds.Should().Be(0);
        });
    }
}
