namespace RateLimiter.Domain;

public interface ITokenBucketAlgorithm
{
    RateLimitDecision Evaluate(TokenBucketState? currentState, RateLimitRule rule, long nowTimestampMs);
}
