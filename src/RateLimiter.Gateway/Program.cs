using RateLimiter.Application;
using RateLimiter.Domain;
using RateLimiter.Gateway;
using RateLimiter.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// OpenAPI / Swagger
builder.Services.AddOpenApi();

builder.Services.Configure<RateLimitMiddlewareOptions>(builder.Configuration.GetSection("RateLimit:Middleware"));

// Domain services
builder.Services.AddSingleton<ITokenBucketAlgorithm, TokenBucketAlgorithm>();

builder.Services.AddApplicationServices()
                .AddInfrastructureServices(builder.Configuration);

builder.Services.AddTransient<RateLimitMiddleware>();

// YARP reverse proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.MapDefaultEndpoints();

// OpenAPI & Swagger UI (development only)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "RateLimiter Gateway API");
    });
}

// Rate limiting runs BEFORE the reverse proxy
app.UseMiddleware<RateLimitMiddleware>();

// YARP reverse proxy forwards allowed requests to backend services
app.MapReverseProxy();

// Local endpoints (fallback for non-proxied routes)
app.MapGet("/", () => "Hello World!");

app.Run();
