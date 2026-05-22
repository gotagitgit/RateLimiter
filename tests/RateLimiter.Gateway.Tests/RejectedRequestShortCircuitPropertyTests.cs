// Feature: distributed-rate-limiter, Property 10: Rejected requests short-circuit the middleware pipeline

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RateLimiter.Domain;
using RateLimiter.Gateway;
using RateLimiter.Infrastructure;
using RateLimiter.Application.Services;
using RateLimiter.Infrastructure.Settings;

namespace RateLimiter.Gateway.Tests;

/// <summary>
/// Property-based tests for rejected request short-circuit behavior.
/// Verifies that when the rate limit decision is IsAllowed = false, the middleware
/// returns HTTP 429 and does NOT invoke the next middleware delegate.
/// </summary>
public class RejectedRequestShortCircuitPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 8.2**
    ///
    /// Property 10: Rejected requests short-circuit the middleware pipeline
    /// For any request where the rate limit decision is IsAllowed = false, the middleware SHALL
    /// return an HTTP 429 response and SHALL NOT invoke the next middleware delegate
    /// (i.e., the request is not forwarded downstream).
    /// </summary>
    [Property]
    public Property RejectedRequest_ShouldReturn429_AndNotInvokeNext()
    {
        var limitGen = Gen.Choose(1, 10000);
        var retryAfterGen = Gen.Choose(1, 3600).Select(x => (double)x);

        var argsGen =
            from limit in limitGen
            from retryAfter in retryAfterGen
            select (limit, retryAfter);

        return Prop.ForAll(argsGen.ToArbitrary(), args =>
        {
            var (limit, retryAfter) = args;

            // Arrange: Create a rejected RateLimitDecision
            var rejectedDecision = RateLimitDecision.Empty() with
            {
                Limit = limit,
                RetryAfterSeconds = retryAfter
            };

            // Mock IClientIdentifier to return a fixed client identifier
            var mockClientIdentifier = Substitute.For<IClientIdentifierService>();
            mockClientIdentifier
                .Extract(Arg.Any<HttpContext>(), Arg.Any<IReadOnlyList<IdentificationStrategy>>())
                .Returns(new ClientIdentifier("test-client", IdentificationStrategy.ApiKey));

            // Mock IRateLimitService to return the rejected decision
            var mockRateLimitService = Substitute.For<IRateLimitService>();
            mockRateLimitService
                .CheckRateLimitAsync(Arg.Any<ClientIdentifier>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(rejectedDecision));

            // Create middleware with mocked dependencies
            var logger = Substitute.For<ILogger<RateLimitMiddleware>>();
            var middlewareOptions = Options.Create(new RateLimitMiddlewareOptions());
            var rateLimitOptions = Options.Create(new RateLimitOptions());

            var middleware = new RateLimitMiddleware(
                mockClientIdentifier,
                mockRateLimitService,
                logger,
                middlewareOptions,
                rateLimitOptions);

            // Create a RequestDelegate that sets a flag when called
            var nextWasCalled = false;
            RequestDelegate next = _ =>
            {
                nextWasCalled = true;
                return Task.CompletedTask;
            };

            // Act: Invoke the middleware
            var context = new DefaultHttpContext();
            middleware.InvokeAsync(context, next).GetAwaiter().GetResult();

            // Assert: Status code is 429
            context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests,
                because: "rejected requests must return HTTP 429");

            // Assert: Next delegate was NOT called
            nextWasCalled.Should().BeFalse(
                because: "rejected requests must short-circuit the pipeline and not invoke the next delegate");

            // Assert: Retry-After header is present
            context.Response.Headers.ContainsKey("Retry-After").Should().BeTrue(
                because: "rejected responses must include a Retry-After header");
        });
    }
}
