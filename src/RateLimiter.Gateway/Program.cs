using RateLimiter.Application;
using RateLimiter.Domain;
using RateLimiter.Gateway;
using RateLimiter.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RateLimitMiddlewareOptions>(builder.Configuration.GetSection("RateLimit:Middleware"));

// Domain services
builder.Services.AddSingleton<ITokenBucketAlgorithm, TokenBucketAlgorithm>();

builder.Services.AddApplicationServices()
                .AddInfrastructureServices(builder.Configuration);

builder.Services.AddTransient<RateLimitMiddleware>();

var app = builder.Build();

app.UseMiddleware<RateLimitMiddleware>();

app.MapGet("/", () => "Hello World!");

app.Run();
