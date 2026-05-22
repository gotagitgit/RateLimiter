namespace RateLimiter.Domain;

public sealed record TokenBucketState(
    double AvailableTokens,
    long LastRefillTimestampMs);
