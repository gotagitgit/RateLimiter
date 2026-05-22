namespace RateLimiter.Domain;

public sealed record RateLimitRule(
    string RuleId,
    int BucketCapacity,
    double RefillRatePerSecond,
    IdentificationStrategy? AppliesTo);
