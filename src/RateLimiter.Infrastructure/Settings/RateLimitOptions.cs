using RateLimiter.Infrastructure.Models;

namespace RateLimiter.Infrastructure.Settings;

/// <summary>
/// Configuration options for the distributed rate limiter's resilience behavior.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>
    /// Determines behavior when the state store is unreachable.
    /// Default: FailClose (reject all requests).
    /// </summary>
    public FailurePolicy FailurePolicy { get; set; } = FailurePolicy.FailClose;

    /// <summary>
    /// Timeout in milliseconds for Redis operations.
    /// Default: 50ms.
    /// </summary>
    public int TimeoutMs { get; set; } = 50;

    /// <summary>
    /// Interval in seconds between health checks when the circuit breaker is open.
    /// Default: 5 seconds.
    /// </summary>
    public int HealthCheckIntervalSeconds { get; set; } = 5;
}
