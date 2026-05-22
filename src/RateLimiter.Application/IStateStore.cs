using RateLimiter.Domain;

namespace RateLimiter.Application;

public interface IStateStore
{
    Task<RateLimitDecision> ExecuteTokenBucketAsync(
        string key,
        int capacity,
        double refillRate,
        CancellationToken ct);
}
