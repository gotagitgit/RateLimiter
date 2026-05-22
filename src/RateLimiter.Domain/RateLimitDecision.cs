namespace RateLimiter.Domain;

public sealed record RateLimitDecision
{
    public bool IsAllowed { get; init; } = false;

    public int Limit { get; init; }

    public int Remaining { get; init; }

    public double RetryAfterSeconds { get; init; }

    public static RateLimitDecision Empty() => new()
    {
        IsAllowed = false,
        Limit = 0,
        Remaining = 0,
        RetryAfterSeconds = 0
    };
}