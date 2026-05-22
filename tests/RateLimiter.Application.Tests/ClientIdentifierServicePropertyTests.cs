// Feature: distributed-rate-limiter, Property 2: Unidentified requests resolve to anonymous

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using RateLimiter.Domain;
using RateLimiter.Application.Services;
using RateLimiter.Application.Services.ClientIdentifiers;
using Microsoft.Extensions.Logging;

namespace RateLimiter.Application.Tests;

/// <summary>
/// **Validates: Requirements 1.3**
///
/// Property 2: Unidentified requests resolve to anonymous
/// For any HTTP request that contains no recognizable identification headers
/// (no valid API Key, User ID, or IP Address), the ClientIdentifierService SHALL return
/// a ClientIdentifier with strategy Anonymous and value "anonymous".
/// </summary>
public class ClientIdentifierServiceAnonymousPropertyTests
{
    private static readonly IdentificationStrategy[][] AllPermutations = GetPermutations(
    [
        IdentificationStrategy.ApiKey,
        IdentificationStrategy.UserId,
        IdentificationStrategy.IpAddress
    ]).ToArray();

    [Property]
    public Property Unidentified_requests_resolve_to_anonymous()
    {
        var arb = Gen.Elements(AllPermutations)
            .Select(p => (IReadOnlyList<IdentificationStrategy>)p.ToList().AsReadOnly())
            .ToArbitrary();

        return Prop.ForAll(arb, priorityOrder =>
        {
            // Arrange: HttpContext with NO identification headers and no RemoteIpAddress
            var httpContext = new DefaultHttpContext();
            // Ensure no identification headers are present
            httpContext.Request.Headers.Remove("X-Api-Key");
            httpContext.Request.Headers.Remove("X-User-Id");
            httpContext.Request.Headers.Remove("X-Forwarded-For");
            httpContext.Connection.RemoteIpAddress = null;

            var identifiers = new IClientIdentifier[]
            {
                new HeaderClientIdentifier(NullLogger<HeaderClientIdentifier>.Instance, "X-Api-Key", IdentificationStrategy.ApiKey),
                new HeaderClientIdentifier(NullLogger<HeaderClientIdentifier>.Instance, "X-User-Id", IdentificationStrategy.UserId),
                new IPAddressClientIdentifier(NullLogger<IPAddressClientIdentifier>.Instance)
            };
            var service = new ClientIdentifierService(identifiers);

            // Act
            var result = service.Extract(httpContext, priorityOrder);

            // Assert
            result.Strategy.Should().Be(IdentificationStrategy.Anonymous,
                because: "no identification headers are present in the request");
            result.Value.Should().Be("anonymous",
                because: "unidentified requests should resolve to the anonymous identifier");
        });
    }

    private static IEnumerable<IdentificationStrategy[]> GetPermutations(IdentificationStrategy[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (int i = 0; i < items.Length; i++)
        {
            var remaining = items.Where((_, idx) => idx != i).ToArray();
            foreach (var perm in GetPermutations(remaining))
            {
                yield return [items[i], .. perm];
            }
        }
    }
}
