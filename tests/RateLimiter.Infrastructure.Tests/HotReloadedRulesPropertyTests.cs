// Feature: distributed-rate-limiter, Property 8: Hot-reloaded rules apply to subsequent evaluations

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FluentAssertions;
using RateLimiter.Application;
using RateLimiter.Domain;

namespace RateLimiter.Infrastructure.Tests;

/// <summary>
/// Property-based tests for hot-reloading of rate limit rules.
/// </summary>
public class HotReloadedRulesPropertyTests
{
    private static readonly double[] RefillRates = [0.5, 1.0, 5.0, 10.0, 50.0, 100.0];

    /// <summary>
    /// **Validates: Requirements 7.3**
    ///
    /// Property 8: Hot-reloaded rules apply to subsequent evaluations
    /// For any initial rule set and for any updated rule set, after the configuration service
    /// detects and applies a rule change, all subsequent rate limit evaluations for affected
    /// clients SHALL use the new rule's capacity and refill rate.
    /// </summary>
    [Property]
    public Property UpdatedRules_ShouldApplyToSubsequentGetRuleCalls()
    {
        var gen =
            from strategy in Gen.Elements(
                IdentificationStrategy.ApiKey,
                IdentificationStrategy.UserId,
                IdentificationStrategy.IpAddress,
                IdentificationStrategy.Anonymous)
            from initialCapacity in Gen.Choose(1, 5000)
            from updatedCapacity in Gen.Choose(5001, 10000)
            from initialRefillIdx in Gen.Choose(0, 2)
            from updatedRefillIdx in Gen.Choose(3, 5)
            select new RuleUpdateInput(
                strategy,
                initialCapacity,
                updatedCapacity,
                RefillRates[initialRefillIdx],
                RefillRates[updatedRefillIdx]);

        return Prop.ForAll(gen.ToArbitrary(), (RuleUpdateInput input) =>
        {
            var provider = new RateLimitRuleProvider();
            var client = new ClientIdentifier($"client-{input.Strategy}", input.Strategy);

            // Build initial rule set
            var initialRules = new List<RateLimitRule>
            {
                new(
                    RuleId: $"initial-{input.Strategy}",
                    BucketCapacity: input.InitialCapacity,
                    RefillRatePerSecond: input.InitialRefillRate,
                    AppliesTo: input.Strategy)
            };

            // Build updated rule set with different capacity and refill rate
            var updatedRules = new List<RateLimitRule>
            {
                new(
                    RuleId: $"updated-{input.Strategy}",
                    BucketCapacity: input.UpdatedCapacity,
                    RefillRatePerSecond: input.UpdatedRefillRate,
                    AppliesTo: input.Strategy)
            };

            // Apply initial rules
            provider.UpdateRules(initialRules);

            // Verify GetRule returns initial rule
            var ruleAfterInitial = provider.GetRule(client);
            ruleAfterInitial.BucketCapacity.Should().Be(input.InitialCapacity,
                because: $"initial rule for {input.Strategy} should have capacity {input.InitialCapacity}");
            ruleAfterInitial.RefillRatePerSecond.Should().Be(input.InitialRefillRate,
                because: $"initial rule for {input.Strategy} should have refill rate {input.InitialRefillRate}");

            // Apply updated rules (simulating hot-reload)
            provider.UpdateRules(updatedRules);

            // Verify GetRule now returns updated rule
            var ruleAfterUpdate = provider.GetRule(client);
            ruleAfterUpdate.BucketCapacity.Should().Be(input.UpdatedCapacity,
                because: $"after hot-reload, rule for {input.Strategy} should have updated capacity {input.UpdatedCapacity}");
            ruleAfterUpdate.RefillRatePerSecond.Should().Be(input.UpdatedRefillRate,
                because: $"after hot-reload, rule for {input.Strategy} should have updated refill rate {input.UpdatedRefillRate}");
        });
    }

    private sealed record RuleUpdateInput(
        IdentificationStrategy Strategy,
        int InitialCapacity,
        int UpdatedCapacity,
        double InitialRefillRate,
        double UpdatedRefillRate);
}
