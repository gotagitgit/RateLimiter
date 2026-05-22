using RateLimiter.Domain;

namespace RateLimiter.Infrastructure.Configuration;

/// <summary>
/// Abstraction for loading rate limit rules from a central configuration store.
/// </summary>
public interface IConfigurationStore
{
    /// <summary>
    /// Loads the current set of rate limit rules from the configuration store.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current list of rate limit rules.</returns>
    Task<IReadOnlyList<RateLimitRule>> LoadRulesAsync(CancellationToken ct);
}
