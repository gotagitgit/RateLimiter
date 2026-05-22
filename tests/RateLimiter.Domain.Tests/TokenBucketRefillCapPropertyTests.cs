// Feature: distributed-rate-limiter, Property 5: Token refill is capped at bucket capacity

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FluentAssertions;
using RateLimiter.Domain;

namespace RateLimiter.Domain.Tests;

/// <summary>
/// **Validates: Requirements 2.1, 2.4, 2.5**
///
/// Property 5: Token refill is capped at bucket capacity
/// For any token bucket with capacity C, refill rate R, current tokens T, and elapsed time E,
/// the token count after refill SHALL equal min(C, T + E * R).
/// The token count SHALL never exceed the bucket capacity.
/// </summary>
public class TokenBucketRefillCapPropertyTests
{
    [Property]
    public Property TokenRefillNeverExceedsBucketCapacity()
    {
        var arb = Arb.From(
            from capacity in Gen.Choose(1, 10000)
            from refillRateInt in Gen.Choose(1, 10000)
            let refillRate = refillRateInt / 10.0 // range 0.1 to 1000.0
            from currentTokensInt in Gen.Choose(0, capacity * 100)
            let currentTokens = Math.Min((double)capacity, currentTokensInt / 100.0)
            from elapsedMs in Gen.Choose(0, 100000).Select(x => (long)x)
            select new
            {
                Capacity = capacity,
                RefillRate = refillRate,
                CurrentTokens = currentTokens,
                ElapsedMs = elapsedMs
            });

        return Prop.ForAll(arb, input =>
        {
            var state = new TokenBucketState(
                AvailableTokens: input.CurrentTokens,
                LastRefillTimestampMs: 0);

            var rule = new RateLimitRule(
                RuleId: "test-rule",
                BucketCapacity: input.Capacity,
                RefillRatePerSecond: input.RefillRate,
                AppliesTo: null);

            var algorithm = new TokenBucketAlgorithm();
            var decision = algorithm.Evaluate(state, rule, nowTimestampMs: input.ElapsedMs);

            // The effective tokens after refill (before consumption) = min(C, T + elapsed_seconds * R)
            double elapsedSeconds = input.ElapsedMs / 1000.0;
            double expectedTokensAfterRefill = Math.Min(input.Capacity, input.CurrentTokens + elapsedSeconds * input.RefillRate);

            // Core property: Remaining never exceeds capacity
            decision.Remaining.Should().BeLessThanOrEqualTo(input.Capacity,
                "remaining tokens must never exceed bucket capacity");

            // Remaining is always non-negative
            decision.Remaining.Should().BeGreaterThanOrEqualTo(0,
                "remaining tokens must never be negative");

            // Verify the refill logic: if allowed, Remaining = floor(min(C, T + E*R) - 1)
            // if rejected, Remaining = 0
            if (decision.IsAllowed)
            {
                int expectedRemaining = (int)Math.Floor(expectedTokensAfterRefill - 1.0);
                decision.Remaining.Should().Be(expectedRemaining,
                    "when allowed, remaining should be floor(tokensAfterRefill - 1)");
            }
            else
            {
                decision.Remaining.Should().Be(0,
                    "when rejected, remaining should be 0");
            }
        });
    }
}
