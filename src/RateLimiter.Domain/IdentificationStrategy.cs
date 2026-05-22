namespace RateLimiter.Domain;

public enum IdentificationStrategy
{
    ApiKey,
    UserId,
    IpAddress,
    Anonymous
}
