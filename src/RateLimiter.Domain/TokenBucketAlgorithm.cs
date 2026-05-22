namespace RateLimiter.Domain;

public sealed class TokenBucketAlgorithm : ITokenBucketAlgorithm
{
    public RateLimitDecision Evaluate(TokenBucketState? currentState, RateLimitRule rule, long nowTimestampMs)
    {
        double tokens;
        long lastTimestamp;

        if (currentState is null)
        {
            // First request: initialize with full bucket capacity
            tokens = rule.BucketCapacity;
            lastTimestamp = nowTimestampMs;
        }
        else
        {
            tokens = currentState.AvailableTokens;
            lastTimestamp = currentState.LastRefillTimestampMs;
        }

        // Calculate elapsed time since last refill
        long elapsedMs = Math.Max(0, nowTimestampMs - lastTimestamp);

        // Add tokens based on elapsed time
        double newTokens = elapsedMs * rule.RefillRatePerSecond / 1000.0;

        // Cap tokens at capacity
        tokens = Math.Min(rule.BucketCapacity, tokens + newTokens);

        if (tokens >= 1.0)
        {
            // Allow: decrement by 1
            tokens -= 1.0;
            int remaining = (int)Math.Floor(tokens);

            return RateLimitDecision.Empty() with
            {
                IsAllowed = true,
                Limit = rule.BucketCapacity,
                Remaining = remaining
            };
        }
        else
        {
            // Reject: calculate retry-after in seconds
            double retryAfterSeconds = Math.Ceiling((1.0 - tokens) / rule.RefillRatePerSecond);

            return RateLimitDecision.Empty() with
            {
                Limit = rule.BucketCapacity,
                RetryAfterSeconds = retryAfterSeconds
            };
        }
    }
}
