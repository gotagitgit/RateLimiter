// Feature: distributed-rate-limiter, Property 6: Response headers reflect decision state

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RateLimiter.Domain;
using RateLimiter.Gateway;
using RateLimiter.Infrastructure;
using RateLimiter.Application.Services;
using RateLimiter.Infrastructure.Settings;

namespace RateLimiter.Gateway.Tests;

/// <summary>
/// Property-based tests for response header correctness.
/// Verifies that rate limit response headers accurately reflect the decision state.
/// </summary>
public class ResponseHeadersPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 3.3, 3.4**
    ///
    /// Property 6: Response headers reflect decision state
    /// For any RateLimitDecision, the generated response headers SHALL include
    /// X-Rate-Limit-Limit equal to the bucket capacity and X-Rate-Limit-Remaining
    /// equal to the remaining tokens. Additionally, when IsAllowed = false, the response
    /// SHALL include a Retry-After header with a value > 0.
    /// </summary>
    [Property]
    public Property ResponseHeaders_ShouldReflectDecisionState()
    {
        var decisionGen =
            from isAllowed in Gen.Elements(true, false)
            from limit in Gen.Choose(1, 10000)
            from remaining in Gen.Choose(0, 10000)
            from retryAfter in isAllowed
                ? Gen.Constant(0.0)
                : Gen.Choose(1, 3600).Select(x => (double)x)
            select RateLimitDecision.Empty() with
            {
                IsAllowed = isAllowed,
                Limit = limit,
                Remaining = remaining,
                RetryAfterSeconds = retryAfter
            };

        return Prop.ForAll(decisionGen.ToArbitrary(), decision =>
        {
            // Arrange
            var clientIdentifier = Substitute.For<IClientIdentifierService>();
            clientIdentifier
                .Extract(Arg.Any<HttpContext>(), Arg.Any<IReadOnlyList<IdentificationStrategy>>())
                .Returns(new ClientIdentifier("test-client", IdentificationStrategy.ApiKey));

            var rateLimitService = Substitute.For<IRateLimitService>();
            rateLimitService
                .CheckRateLimitAsync(Arg.Any<ClientIdentifier>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(decision));

            var logger = NullLogger<RateLimitMiddleware>.Instance;
            var middlewareOptions = Options.Create(new RateLimitMiddlewareOptions());
            var rateLimitOptions = Options.Create(new RateLimitOptions());

            var middleware = new RateLimitMiddleware(
                clientIdentifier,
                rateLimitService,
                logger,
                middlewareOptions,
                rateLimitOptions);

            var context = new DefaultHttpContext();
            RequestDelegate next = _ => Task.CompletedTask;

            // Act
            middleware.InvokeAsync(context, next).GetAwaiter().GetResult();

            // Assert: X-Rate-Limit-Limit always matches decision.Limit
            context.Response.Headers["X-Rate-Limit-Limit"].ToString()
                .Should().Be(decision.Limit.ToString(),
                    because: "X-Rate-Limit-Limit must equal the bucket capacity");

            // Assert: X-Rate-Limit-Remaining always matches decision.Remaining
            context.Response.Headers["X-Rate-Limit-Remaining"].ToString()
                .Should().Be(decision.Remaining.ToString(),
                    because: "X-Rate-Limit-Remaining must equal the remaining tokens");

            if (!decision.IsAllowed)
            {
                // Assert: Retry-After is present and > 0 when rejected
                var retryAfterHeader = context.Response.Headers["Retry-After"].ToString();
                retryAfterHeader.Should().NotBeNullOrEmpty(
                    because: "Retry-After header must be present when request is rejected");

                var retryAfterValue = double.Parse(retryAfterHeader);
                retryAfterValue.Should().BeGreaterThan(0,
                    because: "Retry-After must be > 0 when request is rejected");
            }
            else
            {
                // Assert: Retry-After is NOT present when allowed
                context.Response.Headers.ContainsKey("Retry-After")
                    .Should().BeFalse(
                        because: "Retry-After header must not be present when request is allowed");
            }
        });
    }
}
