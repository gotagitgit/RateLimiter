using RateLimiter.Domain;

namespace RateLimiter.Application;

public interface IRateLimitRuleProvider
{
    RateLimitRule GetRule(ClientIdentifier client);
    RateLimitRule GetDefaultRule();
}
