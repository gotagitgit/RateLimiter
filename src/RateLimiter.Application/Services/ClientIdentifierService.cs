using Microsoft.AspNetCore.Http;
using RateLimiter.Application.Services.ClientIdentifiers;
using RateLimiter.Domain;

namespace RateLimiter.Application.Services;

internal sealed class ClientIdentifierService : IClientIdentifierService
{
    private readonly Dictionary<IdentificationStrategy, IClientIdentifier> _identifiers;

    public ClientIdentifierService(IEnumerable<IClientIdentifier> identifiers)
    {
        _identifiers = identifiers.ToDictionary(i => i.Strategy);
    }

    public ClientIdentifier Extract(HttpContext context, IReadOnlyList<IdentificationStrategy> priorityOrder)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(priorityOrder);

        foreach (var strategy in priorityOrder)
        {
            if (!_identifiers.TryGetValue(strategy, out var identifier))
            {
                throw new InvalidOperationException(
                    $"No IClientIdentifier registered for strategy '{strategy}'.");
            }

            if (identifier.TryExtract(context, out var clientIdentifier))
            {
                return clientIdentifier;
            }
        }

        return ClientIdentifierBase.AnonymousIdentifier;
    }
}
