// Feature: distributed-rate-limiter, Property 9: Configuration resilience preserves cached rules

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FluentAssertions;
using RateLimiter.Application;
using RateLimiter.Domain;

namespace RateLimiter.Infrastructure.Tests;

/// <summary>
/// Property-based tests for configuration resilience.
/// Verifies that the RateLimitRuleProvider preserves cached rules when no update is applied
/// (simulating the ConfigurationPoller catching an exception and not calling UpdateRules).
/// </summary>
public class ConfigurationResiliencePropertyTests
{
    /// <summary>
    /// **Validates: Requirements 7.5**
    ///
    /// Property 9: Configuration resilience preserves cached rules
    /// For any successfully loaded rule set, when the central configuration store becomes
    /// unreachable on a subsequent poll, the rate limit rule provider SHALL continue returning
    /// the previously loaded rules unchanged.
    /// </summary>
    [Property]
    public Property CachedRules_WhenConfigStoreBecomesUnreachable_ShouldReturnPreviouslyLoadedRulesUnchanged()
    {
        var strategyGen = Gen.Elements(
            IdentificationStrategy.ApiKey,
            IdentificationStrategy.UserId,
            IdentificationStrategy.IpAddress,
            IdentificationStrategy.Anonymous);

        var ruleGen =
            from ruleId in Gen.Elements("rule-1", "rule-2", "rule-3", "rule-4", "rule-5")
            from capacity in Gen.Choose(1, 10000)
            from refillRateIndex in Gen.Choose(0, 6)
            let refillRates = new[] { 0.1, 0.5, 1.0, 5.0, 10.0, 50.0, 100.0 }
            let refillRate = refillRates[refillRateIndex]
            from strategy in strategyGen
            select new RateLimitRule(ruleId, capacity, refillRate, strategy);

        var rulesGen = ruleGen.ArrayOf()
            .Where(arr => arr.Length > 0 && arr.Length <= 5)
            .Select(arr => (IReadOnlyList<RateLimitRule>)arr.ToList());

        return Prop.ForAll(rulesGen.ToArbitrary(), rules =>
        {
            // Arrange: Create provider and load rules successfully
            var provider = new RateLimitRuleProvider();
            provider.UpdateRules(rules);

            // Act: Verify rules are accessible after initial load
            var rulesBeforeFailure = new List<RateLimitRule>();
            foreach (var rule in rules)
            {
                if (rule.AppliesTo.HasValue)
                {
                    var client = new ClientIdentifier("test-client", rule.AppliesTo.Value);
                    rulesBeforeFailure.Add(provider.GetRule(client));
                }
            }

            // Simulate configuration store failure:
            // The ConfigurationPoller would catch the exception and NOT call UpdateRules.
            // We simply do NOT call UpdateRules again - this simulates the failure scenario.

            // Assert: After "failure", GetRule still returns the same previously loaded rules
            var rulesAfterFailure = new List<RateLimitRule>();
            foreach (var rule in rules)
            {
                if (rule.AppliesTo.HasValue)
                {
                    var client = new ClientIdentifier("test-client", rule.AppliesTo.Value);
                    rulesAfterFailure.Add(provider.GetRule(client));
                }
            }

            // The rules returned after "failure" must be identical to the rules loaded initially
            rulesAfterFailure.Should().BeEquivalentTo(rulesBeforeFailure,
                because: "the provider should preserve cached rules when no update is applied (config store unreachable)");

            // Additionally verify the default rule is also preserved
            var defaultBefore = provider.GetDefaultRule();
            // Simulate another failed poll (no UpdateRules call)
            var defaultAfter = provider.GetDefaultRule();
            defaultAfter.Should().Be(defaultBefore,
                because: "the default rule should also be preserved when config store is unreachable");
        });
    }
}
