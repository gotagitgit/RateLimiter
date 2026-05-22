namespace RateLimiter.Infrastructure.Configuration;

/// <summary>
/// Options for the <see cref="ConfigurationPoller"/> hosted service.
/// </summary>
public sealed class ConfigurationPollerOptions
{
    /// <summary>
    /// The interval at which the configuration store is polled for rule changes.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
}
