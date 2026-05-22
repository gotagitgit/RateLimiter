using Microsoft.AspNetCore.Http;
using RateLimiter.Domain;

namespace RateLimiter.Application.Services;

public interface IClientIdentifierService
{
    ClientIdentifier Extract(HttpContext context, IReadOnlyList<IdentificationStrategy> priorityOrder);
}
