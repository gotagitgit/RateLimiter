using Microsoft.Extensions.Options;
using RateLimiter.Application.Services;
using RateLimiter.Infrastructure.Models;
using RateLimiter.Infrastructure.Settings;

namespace RateLimiter.Gateway;

/// <summary>
/// ASP.NET Core middleware that enforces per-client rate limits.
/// Extracts client identity, checks the rate limit service, and either
/// forwards the request or returns HTTP 429 with appropriate headers.
/// Never throws exceptions to the outer pipeline.
/// </summary>
public sealed class RateLimitMiddleware : IMiddleware
{
    private readonly IClientIdentifierService _clientIdentifier;
    private readonly IRateLimitService _rateLimitService;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly RateLimitMiddlewareOptions _middlewareOptions;
    private readonly RateLimitOptions _rateLimitOptions;

    public RateLimitMiddleware(
        IClientIdentifierService clientIdentifier,
        IRateLimitService rateLimitService,
        ILogger<RateLimitMiddleware> logger,
        IOptions<RateLimitMiddlewareOptions> middlewareOptions,
        IOptions<RateLimitOptions> rateLimitOptions)
    {
        _clientIdentifier = clientIdentifier;
        _rateLimitService = rateLimitService;
        _logger = logger;
        _middlewareOptions = middlewareOptions.Value;
        _rateLimitOptions = rateLimitOptions.Value;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            var client = _clientIdentifier.Extract(context, _middlewareOptions.IdentificationPriority);
            var decision = await _rateLimitService.CheckRateLimitAsync(client, context.RequestAborted);

            if (decision.IsAllowed)
            {
                context.Response.Headers["X-Rate-Limit-Limit"] = decision.Limit.ToString();
                context.Response.Headers["X-Rate-Limit-Remaining"] = decision.Remaining.ToString();
                await next(context);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers["X-Rate-Limit-Limit"] = decision.Limit.ToString();
                context.Response.Headers["X-Rate-Limit-Remaining"] = decision.Remaining.ToString();
                context.Response.Headers["Retry-After"] = Math.Ceiling(decision.RetryAfterSeconds).ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rate limit middleware encountered an error. Applying failure policy: {Policy}", _rateLimitOptions.FailurePolicy);

            if (_rateLimitOptions.FailurePolicy == FailurePolicy.FailOpen)
            {
                await next(context);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            }
        }
    }
}
