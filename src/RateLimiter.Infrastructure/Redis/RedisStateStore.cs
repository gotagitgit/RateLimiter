using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using RateLimiter.Application;
using RateLimiter.Domain;
using RateLimiter.Infrastructure.Models;
using RateLimiter.Infrastructure.Settings;
using StackExchange.Redis;

namespace RateLimiter.Infrastructure.Redis;

public sealed class RedisStateStore : IStateStore
{
    private const string RedisPipelineName = "redis";

    private readonly IConnectionMultiplexer _redis;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly RateLimitOptions _options;

    public RedisStateStore(
        IConnectionMultiplexer redis,
        ResiliencePipelineProvider<string> pipelineProvider,
        IOptions<RateLimitOptions> options)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        ArgumentNullException.ThrowIfNull(pipelineProvider);
        ArgumentNullException.ThrowIfNull(options);
        _resiliencePipeline = pipelineProvider.GetPipeline(RedisPipelineName);
        _options = options.Value;
    }

    public async Task<RateLimitDecision> ExecuteTokenBucketAsync(
        string key,
        int capacity,
        double refillRate,
        CancellationToken ct)
    {
        try
        {
            return await _resiliencePipeline.ExecuteAsync(async token =>
            {
                var db = _redis.GetDatabase();

                // Get current time from Redis to ensure consistent timestamps across gateway instances
                var currentTimeInMs = await GetDatabaseTimeAsync(db);

                var result = await db.ScriptEvaluateAsync(
                    LuaScripts.TokenBucket,
                    [key],
                    [capacity, refillRate, currentTimeInMs]);

                var values = (RedisResult[])result!;
                int allowed = (int)values[0];
                int remaining = (int)values[1];
                long retryAfterMs = (long)values[2];
                int returnedCapacity = (int)values[3];

                double retryAfterSeconds = retryAfterMs / 1000.0;

                return RateLimitDecision.Empty() with
                {
                    IsAllowed = allowed == 1,
                    Limit = returnedCapacity,
                    Remaining = remaining,
                    RetryAfterSeconds = retryAfterSeconds
                };
            }, ct);
        }
        catch (Exception)
        {
            // Circuit breaker open, timeout, or Redis failure — apply failure policy
            return _options.FailurePolicy switch
            {
                FailurePolicy.FailOpen => RateLimitDecision.Empty() with
                {
                    IsAllowed = true,
                    Limit = capacity,
                    Remaining = capacity
                },

                // FailClose is the default
                _ => RateLimitDecision.Empty() with
                {
                    Limit = capacity,
                    RetryAfterSeconds = 1
                }
            };
        }
    }

    private static async ValueTask<long> GetDatabaseTimeAsync(IDatabase db)
    {
        var timeResult = await db.ExecuteAsync("TIME");
        var timeArray = (RedisResult[])timeResult!;
        
        long seconds = (long)timeArray[0];
        long microseconds = (long)timeArray[1];
        long nowMs = seconds * 1000 + microseconds / 1000;
        
        return nowMs;
    }
}
