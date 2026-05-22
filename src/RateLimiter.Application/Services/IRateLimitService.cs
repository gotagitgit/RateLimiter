using RateLimiter.Domain;

namespace RateLimiter.Application.Services;

public interface IRateLimitService
{
    Task<RateLimitDecision> CheckRateLimitAsync(ClientIdentifier client, CancellationToken ct);
}
