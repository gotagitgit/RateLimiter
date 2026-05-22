using RateLimiter.Domain;

namespace RateLimiter.Application;

public sealed class RateLimitRuleProvider : IRateLimitRuleProvider
{
    private static readonly RateLimitRule FallbackRule = new(
        RuleId: "fallback-anonymous",
        BucketCapacity: 100,
        RefillRatePerSecond: 10,
        AppliesTo: IdentificationStrategy.Anonymous);

    /// <summary>
    /// The rule set is marked volatile to ensure cross-thread visibility: when the
    /// ConfigurationPoller swaps this reference, request threads immediately see the new list.
    /// Methods capture a local snapshot before iterating to guarantee consistency within a
    /// single call — even if UpdateRules swaps the reference mid-execution.
    /// </summary>
    private volatile IReadOnlyList<RateLimitRule> _rules = [];

    public RateLimitRule GetRule(ClientIdentifier client)
    {
        ArgumentNullException.ThrowIfNull(client);

        var snapshot = _rules;

        foreach (var rule in snapshot)
        {
            if (rule.AppliesTo == client.Strategy)
            {
                return rule;
            }
        }

        return GetDefaultRule(snapshot);
    }

    public RateLimitRule GetDefaultRule()
    => GetDefaultRule(_rules);

    private RateLimitRule GetDefaultRule(IReadOnlyList<RateLimitRule> rules)
    {
        foreach (var rule in rules)
        {
            if (rule.AppliesTo == IdentificationStrategy.Anonymous)
                return rule;
        }

        return FallbackRule;
    }

    public void UpdateRules(IReadOnlyList<RateLimitRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
    }
}
