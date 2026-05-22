namespace RateLimiter.Domain;

public sealed record ClientIdentifier(string Value, IdentificationStrategy Strategy);
