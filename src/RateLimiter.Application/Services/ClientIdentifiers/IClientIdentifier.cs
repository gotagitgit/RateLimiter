using Microsoft.AspNetCore.Http;
using RateLimiter.Domain;

namespace RateLimiter.Application.Services.ClientIdentifiers;

internal interface IClientIdentifier
{
    IdentificationStrategy Strategy { get; }

    bool TryExtract(HttpContext context, out ClientIdentifier clientIdentifier);
}