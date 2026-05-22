using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RateLimiter.Domain;

namespace RateLimiter.Application.Services.ClientIdentifiers;

internal sealed class IPAddressClientIdentifier : ClientIdentifierBase
{
    private const string ForwardedForHeader = "X-Forwarded-For";

    public IPAddressClientIdentifier(ILogger<IPAddressClientIdentifier> logger)
        : base(logger)
    {
    }

    public override IdentificationStrategy Strategy => IdentificationStrategy.IpAddress;

    public override bool TryExtract(HttpContext context, out ClientIdentifier clientIdentifier)
    {
        var forwardedFor = context.Request.Headers[ForwardedForHeader].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var clientIp = forwardedFor.Split(',', StringSplitOptions.TrimEntries)[0];

            if (!string.IsNullOrWhiteSpace(clientIp))
            {
                clientIdentifier = new ClientIdentifier(Truncate(clientIp), IdentificationStrategy.IpAddress);
                return true;
            }
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();

        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            clientIdentifier = new ClientIdentifier(Truncate(remoteIp), IdentificationStrategy.IpAddress);
            return true;
        }

        clientIdentifier = AnonymousIdentifier;
        return false;
    }
}
