namespace RateLimiter.Infrastructure.Models;

/// <summary>
/// Determines behavior when the state store is unreachable or a timeout occurs.
/// </summary>
public enum FailurePolicy
{
    /// <summary>
    /// Reject all requests when the state store is unavailable (default, prioritizes backend protection).
    /// </summary>
    FailClose,

    /// <summary>
    /// Allow all requests when the state store is unavailable.
    /// </summary>
    FailOpen
}
