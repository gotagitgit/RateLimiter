namespace RateLimiter.Infrastructure.Redis;

/// <summary>
/// Contains Lua scripts executed atomically on Redis for rate limiting operations.
/// </summary>
public static class LuaScripts
{
    /// <summary>
    /// Atomic Token Bucket Lua script.
    /// <para>
    /// KEYS[1] = bucket key (e.g., "rl:{clientIdentifier}")
    /// ARGV[1] = bucket capacity
    /// ARGV[2] = refill rate (tokens per second)
    /// ARGV[3] = current time in milliseconds (from Redis TIME)
    /// </para>
    /// <para>
    /// Returns: {allowed (0|1), remaining, retry_after_ms, capacity}
    /// </para>
    /// </summary>
    public const string TokenBucket = """
        -- KEYS[1] = bucket key
        -- ARGV[1] = bucket capacity
        -- ARGV[2] = refill rate (tokens per second)
        -- ARGV[3] = current time in milliseconds (from Redis TIME)

        local key = KEYS[1]
        local capacity = tonumber(ARGV[1])
        local refill_rate = tonumber(ARGV[2])
        local now_ms = tonumber(ARGV[3])

        local bucket = redis.call('HMGET', key, 'tokens', 'ts')
        local tokens = tonumber(bucket[1])
        local last_ts = tonumber(bucket[2])

        if tokens == nil then
            -- First request: initialize full bucket
            tokens = capacity
            last_ts = now_ms
        end

        -- Calculate tokens to add based on elapsed time
        local elapsed_ms = math.max(0, now_ms - last_ts)
        local new_tokens = elapsed_ms * refill_rate / 1000.0
        tokens = math.min(capacity, tokens + new_tokens)

        local allowed = 0
        local remaining = 0
        local retry_after = 0

        if tokens >= 1 then
            tokens = tokens - 1
            allowed = 1
            remaining = math.floor(tokens)
        else
            allowed = 0
            remaining = 0
            retry_after = math.ceil((1 - tokens) / refill_rate * 1000) -- ms until next token
        end

        -- Persist state
        redis.call('HSET', key, 'tokens', tostring(tokens), 'ts', tostring(now_ms))

        -- Set TTL to prevent stale keys from accumulating
        local ttl = math.ceil(capacity / refill_rate * 2)
        redis.call('EXPIRE', key, ttl)

        return {allowed, remaining, retry_after, capacity}
        """;
}
