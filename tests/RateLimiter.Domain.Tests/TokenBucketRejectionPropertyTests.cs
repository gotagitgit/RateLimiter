// Feature: distributed-rate-limiter, Property 4: Rejection when bucket is empty

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FluentAssertions;
using RateLimiter.Domain;

namespace RateLimiter.Domain.Tests;

/// <summary>
/// Property-based tests for token bucket rejection behavior.
/// </summary>
public class TokenBucketRejectionPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 2.3, 3.2**
    ///
    /// Property 4: Rejection when bucket is empty
    /// For any token bucket state where available tokens &lt; 1 and no refill time has elapsed,
    /// evaluating a rate limit check SHALL return IsAllowed = false with Remaining = 0.
    /// </summary>
    [Property]
    public Property Rejection_WhenBucketEmpty_ShouldDenyWithZeroRemaining()
    {
        var arb = Arb.From(
            from availableTokens in Gen.Choose(0, 99).Select(i => i / 100.0) // range [0, 0.99]
            from lastRefillTimestampMs in Gen.Choose(0, 1_000_000_000).Select(i => (long)i)
            from bucketCapacity in Gen.Choose(1, 10000)
            from refillRate in Gen.Elements(0.1, 0.5, 1.0, 5.0, 10.0, 50.0, 100.0)
            select new
            {
                State = new TokenBucketState(availableTokens, lastRefillTimestampMs),
                Rule = new RateLimitRule("test-rule", bucketCapacity, refillRate, null),
                NowTimestampMs = lastRefillTimestampMs // zero elapsed time, so no refill occurs
            });

        return Prop.ForAll(arb, input =>
        {
            var algorithm = new TokenBucketAlgorithm();

            var result = algorithm.Evaluate(input.State, input.Rule, input.NowTimestampMs);

            // The request should be rejected
            result.IsAllowed.Should().BeFalse(
                because: $"available tokens ({input.State.AvailableTokens}) < 1 and no refill time elapsed");

            // Remaining should be 0
            result.Remaining.Should().Be(0,
                because: "no tokens are available when bucket is empty");

            // RetryAfterSeconds should be > 0 (time until next token)
            result.RetryAfterSeconds.Should().BeGreaterThan(0,
                because: "client must wait for tokens to refill");
        });
    }
}
