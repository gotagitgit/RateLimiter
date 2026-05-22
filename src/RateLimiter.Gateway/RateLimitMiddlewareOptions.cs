using RateLimiter.Domain;

namespace RateLimiter.Gateway;

public sealed class RateLimitMiddlewareOptions
{
    public List<IdentificationStrategy> IdentificationPriority { get; set; } =
        [IdentificationStrategy.ApiKey, IdentificationStrategy.UserId, IdentificationStrategy.IpAddress];
}
