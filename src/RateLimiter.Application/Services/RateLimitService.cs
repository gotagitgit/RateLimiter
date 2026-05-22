using RateLimiter.Domain;

namespace RateLimiter.Application.Services;

internal sealed class RateLimitService : IRateLimitService
{
    private readonly IRateLimitRuleProvider _ruleProvider;
    private readonly IStateStore _stateStore;

    public RateLimitService(IRateLimitRuleProvider ruleProvider, IStateStore stateStore)
    {
        _ruleProvider = ruleProvider ?? throw new ArgumentNullException(nameof(ruleProvider));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public async Task<RateLimitDecision> CheckRateLimitAsync(ClientIdentifier client, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);

        var rule = _ruleProvider.GetRule(client);
        var key = $"rl:{client.Value}";

        return await _stateStore.ExecuteTokenBucketAsync(
            key,
            rule.BucketCapacity,
            rule.RefillRatePerSecond,
            ct);
    }
}
