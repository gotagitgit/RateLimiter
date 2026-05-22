using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RateLimiter.Domain;

namespace RateLimiter.Application.Services.ClientIdentifiers;

internal abstract class ClientIdentifierBase : IClientIdentifier
{
    private const int MaxHeaderLength = 256;

    protected readonly ILogger _logger;

    protected ClientIdentifierBase(ILogger logger)
    {
        _logger = logger;
    }

    public static ClientIdentifier AnonymousIdentifier => new("anonymous", IdentificationStrategy.Anonymous);

    public abstract IdentificationStrategy Strategy { get; }

    public abstract bool TryExtract(HttpContext context, out ClientIdentifier clientIdentifier);

    protected string Truncate(string value)
    {
        if (value.Length > MaxHeaderLength)
        {
            _logger.LogWarning(
                "Client identifier value exceeded maximum length of {MaxLength} characters and was truncated",
                MaxHeaderLength);
            return value[..MaxHeaderLength];
        }

        return value;
    }
}
