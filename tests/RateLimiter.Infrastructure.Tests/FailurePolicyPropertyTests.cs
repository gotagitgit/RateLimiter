// Feature: distributed-rate-limiter, Property 7: Failure policy determines behavior on state store unavailability

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Polly;
using Polly.Registry;
using RateLimiter.Infrastructure.Redis;
using StackExchange.Redis;
using RateLimiter.Infrastructure.Models;
using RateLimiter.Infrastructure.Settings;

namespace RateLimiter.Infrastructure.Tests;

/// <summary>
/// Property-based tests for failure policy behavior.
/// Verifies that when the state store is unavailable, the configured failure policy
/// determines whether requests are allowed (fail-open) or rejected (fail-close).
/// </summary>
public class FailurePolicyPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 6.1, 6.3**
    ///
    /// Property 7: Failure policy determines behavior on state store unavailability
    /// For any configured failure policy (fail-open or fail-close), when the state store is
    /// unreachable or a timeout occurs, the rate limiter SHALL allow the request if the policy
    /// is fail-open, or reject the request if the policy is fail-close.
    /// </summary>
    [Property]
    public Property FailurePolicy_WhenStateStoreUnavailable_ShouldDetermineAllowOrReject()
    {
        var policyGen = Gen.Elements(FailurePolicy.FailOpen, FailurePolicy.FailClose);
        var capacityGen = Gen.Choose(1, 10000);
        var refillRateGen = Gen.Choose(1, 100).Select(x => (double)x);

        var argsGen =
            from policy in policyGen
            from capacity in capacityGen
            from refillRate in refillRateGen
            select (policy, capacity, refillRate);

        return Prop.ForAll(argsGen.ToArbitrary(), args =>
        {
            var (policy, capacity, refillRate) = args;

            // Arrange: Mock IConnectionMultiplexer to throw RedisConnectionException
            var mockRedis = Substitute.For<IConnectionMultiplexer>();
            mockRedis.GetDatabase(Arg.Any<int>(), Arg.Any<object>())
                .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated unavailability"));

            var options = new RateLimitOptions { FailurePolicy = policy };

            // Use a mock pipeline provider that returns ResiliencePipeline.Empty
            var mockPipelineProvider = Substitute.For<ResiliencePipelineProvider<string>>();
            mockPipelineProvider.GetPipeline("redis").Returns(ResiliencePipeline.Empty);

            var store = new RedisStateStore(mockRedis, mockPipelineProvider, Options.Create(options));

            // Act
            var result = store.ExecuteTokenBucketAsync(
                "test-key", capacity, refillRate, CancellationToken.None).GetAwaiter().GetResult();

            // Assert
            if (policy == FailurePolicy.FailOpen)
            {
                result.IsAllowed.Should().BeTrue(
                    because: "fail-open policy should allow requests when state store is unavailable");
            }
            else
            {
                result.IsAllowed.Should().BeFalse(
                    because: "fail-close policy should reject requests when state store is unavailable");
            }
        });
    }
}
