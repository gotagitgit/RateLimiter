using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RateLimiter.Domain;

namespace RateLimiter.Application.Services.ClientIdentifiers;

internal sealed class HeaderClientIdentifier : ClientIdentifierBase, IClientIdentifier
{
    private readonly string _headerName;
    private readonly IdentificationStrategy _strategy;

    public HeaderClientIdentifier(ILogger<HeaderClientIdentifier> logger, string headerName, IdentificationStrategy strategy)
        : base(logger)
    {
        _headerName = headerName;
        _strategy = strategy;
    }

    public override IdentificationStrategy Strategy => _strategy;

    public override bool TryExtract(HttpContext context, out ClientIdentifier clientIdentifier)
    {
        var value = context.Request.Headers[_headerName].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(value))
        {
            clientIdentifier = new ClientIdentifier(Truncate(value), Strategy);
            return true;
        }

        clientIdentifier = AnonymousIdentifier;
        return false;
    }

    internal static void RegisterHeaderIdentifier(IServiceCollection services)
    {
        (string Key, IdentificationStrategy Strategy)[] identifiers = 
        [
            ("X-Api-Key", IdentificationStrategy.ApiKey),
            ("X-User-Id", IdentificationStrategy.UserId)
        ];

        foreach (var identifier in identifiers)
        {
            services.AddSingleton<IClientIdentifier>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<HeaderClientIdentifier>>();
                return new HeaderClientIdentifier(logger, identifier.Key, identifier.Strategy);
            });
        }
    }
}