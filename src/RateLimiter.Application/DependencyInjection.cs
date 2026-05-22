using Microsoft.Extensions.DependencyInjection;
using RateLimiter.Application.Services;
using RateLimiter.Application.Services.ClientIdentifiers;

namespace RateLimiter.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<RateLimitRuleProvider>();
        services.AddSingleton<IRateLimitRuleProvider>(sp => sp.GetRequiredService<RateLimitRuleProvider>());
        services.AddSingleton<IClientIdentifierService, ClientIdentifierService>();
        services.AddScoped<IRateLimitService, RateLimitService>();
        
        RegisterClientIdentifiers(services);

        return services;
    }

    private static void RegisterClientIdentifiers(IServiceCollection services)
    {
        services.AddSingleton<IClientIdentifier, IPAddressClientIdentifier>();
        HeaderClientIdentifier.RegisterHeaderIdentifier(services);
    }
}
