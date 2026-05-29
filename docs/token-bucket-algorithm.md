# Token Bucket Algorithm

## The Problem

APIs exposed to the internet (or even internal consumers) face a fundamental challenge: **uncontrolled traffic can overwhelm backend services**. Without rate limiting, a single misbehaving client, a bot, or a coordinated attack can:

- **Exhaust server resources** — CPU, memory, and connection pools get saturated, causing cascading failures.
- **Degrade service for everyone** — Legitimate users experience timeouts and errors because one client is monopolizing capacity.
- **Increase infrastructure costs** — Auto-scaling responds to abusive traffic the same way it responds to real demand, driving up cloud bills.
- **Create unpredictable behavior** — Without explicit limits, clients have no contract for how much throughput they can expect, making integration brittle.

Rate limiting solves this by enforcing a throughput contract: each client gets a defined budget of requests over time. The question is *how* to enforce that budget fairly and efficiently.

---

## Common Rate Limiting Approaches

Before diving into Token Bucket, it helps to understand the landscape of rate limiting algorithms and why some fall short.

### Fixed Window Counter

Divide time into fixed intervals (e.g., 1-minute windows) and count requests per window.

```
    Window 1 (00:00–01:00)     Window 2 (01:00–02:00)
    ┌──────────────────────┐   ┌──────────────────────┐
    │ ████████░░  80/100   │   │ ██░░░░░░░░  20/100   │
    └──────────────────────┘   └──────────────────────┘
```

**Problem — Boundary Burst**: The counter resets at the window boundary, so a client can game the timing:

```
        Window 1                    Window 2
  ┌──────────────────────────┐┌──────────────────────────┐
  │ 00:00            00:59   ││ 01:00            01:59   │
  │                          ││                          │
  │              ████████████││████████████              │
  │              100 requests││100 requests              │
  │              (00:50-00:59)││(01:00-01:09)            │
  └──────────────────────────┘└──────────────────────────┘
                         ▲
                    Window boundary
```

The client sends 100 requests at **00:50–00:59** (end of window 1) and another 100 at **01:00–01:09** (start of window 2). Both pass — the counter sees 100 in each window individually. But the server just absorbed **200 requests in ~19 seconds**:

```
    Intended max throughput:  100 req / 60 sec  ≈  1.7 req/sec
    Actual burst at boundary: 200 req / 19 sec  ≈ 10.5 req/sec  (6x the intended rate)
```

The root cause is that the counter has **no memory across windows**. The moment the clock ticks to the next boundary, the slate is wiped clean regardless of what happened 1 second ago.

### Sliding Window Log

Track the timestamp of every request and count how many fall within the trailing window.

**Problem**: Requires storing every request timestamp. At high throughput this becomes expensive in both memory and computation (scanning the log on every request).

### Sliding Window Counter

A hybrid that interpolates between the current and previous window counts.

**Problem**: Better than fixed window, but still approximates. The effective limit varies depending on where in the window the requests land, making the behavior harder to reason about.

### Leaky Bucket

Requests enter a queue (bucket) and are processed at a fixed rate. If the queue is full, new requests are dropped.

**Problem**: Enforces a strict constant rate with no burst tolerance. A client that was idle for 10 seconds gets no benefit — they still can't send more than 1 request per interval. This penalizes bursty-but-legitimate traffic patterns (e.g., a page load that fires 5 API calls simultaneously).

---

## Token Bucket: The Best of Both Worlds

The Token Bucket algorithm solves the shortcomings above by combining **sustained rate enforcement** with **burst tolerance**.

### Core Concept

Imagine a bucket that holds tokens. Each request costs one token. Tokens are added to the bucket at a steady rate. If the bucket is full, new tokens are discarded (the bucket doesn't overflow). If the bucket is empty, requests are denied.

```
    Token Bucket Lifecycle
    ══════════════════════

    ┌─────────────────────────────────────┐
    │         Token Bucket (Client)       │
    │                                     │
    │   Capacity: 100 tokens              │
    │   Refill Rate: 10 tokens/sec        │
    │                                     │
    │   ┌─┬─┬─┬─┬─┬─┬─┬─┬─┬─┐             │
    │   │●│●│●│●│●│●│●│ │ │ │  ← Tokens   │ 
    │   └─┴─┴─┴─┴─┴─┴─┴─┴─┴─┘             │
    │         7 / 10 available            │
    └─────────────────────────────────────┘
```

### How It Works

1. **Initialize** — The bucket starts full (tokens = capacity).
2. **Request arrives** — Check if tokens ≥ 1.
   - **Yes** → Consume 1 token, allow the request.
   - **No** → Deny the request (HTTP 429). Return a `Retry-After` header indicating when the next token will be available.
3. **Refill (continuous)** — Tokens accumulate at `refill_rate` tokens per second, capped at bucket capacity.

```
    tokens_to_add = elapsed_seconds × refill_rate
    new_token_count = min(current_tokens + tokens_to_add, capacity)
```

### Decision Flow

```
    REQUEST
      │
      ▼
    ┌─────────────────────┐
    │ Calculate tokens    │
    │ added since last    │
    │ request (refill)    │
    └──────────┬──────────┘
               │
               ▼
    ┌─────────────────────┐
    │ Tokens ≥ 1?         │
    └──────────┬──────────┘
          ┌────┴────┐
          │         │
         YES        NO
          │         │
          ▼         ▼
    ┌──────────┐  ┌──────────────────┐
    │ Consume  │  │ Deny (HTTP 429)  │
    │ 1 token  │  │ Retry-After =    │
    │ ALLOW    │  │ (1-tokens)/rate  │
    └──────────┘  └──────────────────┘
```

---

## Key Properties

| Property | Description | Example |
|----------|-------------|---------|
| **Bucket Capacity** | Maximum tokens the bucket can hold. Defines the burst limit. | 100 |
| **Refill Rate** | Tokens added per second. Defines sustained throughput. | 10/sec |
| **Burst Tolerance** | A full bucket allows short bursts up to capacity without waiting. | 100 requests instantly |
| **Smooth Refill** | Tokens accumulate continuously, not at interval boundaries. | No "reset cliff" |
| **Predictable Retry** | When denied, the client knows exactly when to retry. | `Retry-After: 0.1s` |

---

## Why Token Bucket Over Alternatives?

| Concern | Fixed Window | Leaky Bucket | Token Bucket |
|---------|:---:|:---:|:---:|
| Prevents boundary bursts | ❌ | ✅ | ✅ |
| Allows legitimate bursts | ✅ (accidentally) | ❌ | ✅ (by design) |
| Constant memory per client | ✅ | ✅ | ✅ |
| Predictable retry timing | ❌ | ✅ | ✅ |
| Simple to implement atomically | ✅ | ⚠️ | ✅ |

Token Bucket gives you the burst tolerance that Leaky Bucket lacks, without the boundary problems of Fixed Window, all while keeping memory usage constant per client.

---

## Atomicity with Redis

### The Problem: Distributed State

A single gateway instance can keep token buckets in memory — no concurrency issue. But in production you typically run **multiple gateway instances** behind a load balancer for availability and throughput:

```
                    ┌─────────────┐
                    │   Load      │
    Clients ───────►│  Balancer   │
                    └──────┬──────┘
                           │
              ┌────────────┼────────────┐
              │            │            │
              ▼            ▼            ▼
        ┌──────────┐ ┌──────────┐ ┌──────────┐
        │Gateway A │ │Gateway B │ │Gateway C │
        └──────────┘ └──────────┘ └──────────┘
```

If each gateway keeps its own in-memory bucket, a client with a 100-token limit effectively gets **300 tokens** (100 per instance). The rate limit is meaningless. So the bucket state must live in a **shared store** — Redis.

### The Problem: Race Conditions (Read-Then-Write)

Once the state is in Redis, a new problem emerges. The token bucket operation has three steps: **read** the current tokens, **decide** whether to allow, and **write** the updated count. If two gateways execute these steps independently, their operations can interleave:

```
    Timeline (client has 1 token remaining)
    ─────────────────────────────────────────────────────────

    Gateway A                    Redis                    Gateway B
        │                          │                          │
        │── GET tokens ───────────►│                          │
        │◄─── tokens = 1 ──────────│                          │
        │                          │◄──── GET tokens ─────────│
        │                          │───── tokens = 1 ────────►│
        │                          │                          │
        │  (decides: 1 ≥ 1, ALLOW) │                          │  (decides: 1 ≥ 1, ALLOW)
        │                          │                          │
        │── SET tokens = 0 ───────►│                          │
        │                          │◄──── SET tokens = 0 ─────│
        │                          │                          │
        ▼                          ▼                          ▼
    ALLOW ✓                   tokens = 0                  ALLOW ✓

    Result: 2 requests allowed, but only 1 token was available → OVER-LIMIT
```

Both gateways read `tokens = 1`, both independently decide to allow, and both write `tokens = 0`. The client got **2 requests through on a 1-token budget**. At scale with high concurrency, this compounds — your rate limit becomes a suggestion rather than an enforcement.

This is a classic **TOCTOU (Time-of-Check to Time-of-Use)** bug. The state was valid when checked, but changed before the write landed.

### Why Not Use Redis Transactions (MULTI/EXEC)?

Redis has `MULTI/EXEC` for transactions, but they don't solve this. Redis transactions guarantee that commands execute together without interleaving from *other* commands, but they don't provide **conditional logic**. You can't do "read tokens, and IF tokens ≥ 1 THEN decrement" inside a `MULTI` block — the `IF` decision happens on the client side between the read and write, which is exactly where the race condition lives.

`WATCH` + `MULTI/EXEC` (optimistic locking) can work but adds retry loops and complexity. Under high contention, retries pile up and throughput drops.

### The Solution: Atomic Lua Scripts

Redis executes Lua scripts **atomically** — the entire script runs to completion before any other command is processed. This means the read, decide, and write steps happen as a single indivisible operation:

```
    Timeline (client has 1 token remaining)
    ─────────────────────────────────────────────────────────

    Gateway A                    Redis                    Gateway B
        │                          │                          │
        │── EVALSHA (Lua) ────────►│                          │
        │                          │ ┌──────────────────┐     │
        │                          │ │ Read: tokens = 1 │     │
        │                          │ │ Check: 1 ≥ 1 ✓   │     │
        │                          │ │ Write: tokens = 0│     │
        │                          │ │ Return: ALLOW    │     │
        │                          │ └──────────────────┘     │
        │◄─── ALLOW ────────────── │                          │
        │                          │◄──── EVALSHA (Lua) ──────│
        │                          │ ┌──────────────────┐     │
        │                          │ │ Read: tokens = 0 │     │
        │                          │ │ Check: 0 ≥ 1 ✗   │     │
        │                          │ │ Return: DENY     │     │
        │                          │ └──────────────────┘     │
        │                          │───── DENY ──────────────►│
        ▼                          ▼                          ▼
    ALLOW ✓                   tokens = 0                  DENY ✗ (429)

    Result: Exactly 1 request allowed — rate limit correctly enforced
```

Gateway B's script sees the state *after* Gateway A's script completed. No interleaving possible.

```lua
-- Simplified Token Bucket Lua Script
local key = KEYS[1]
local capacity = tonumber(ARGV[1])
local refill_rate = tonumber(ARGV[2])
local now = tonumber(ARGV[3])

-- Get current state
local bucket = redis.call('HMGET', key, 'tokens', 'last_refill')
local tokens = tonumber(bucket[1]) or capacity
local last_refill = tonumber(bucket[2]) or now

-- Calculate refill
local elapsed = (now - last_refill) / 1000
local new_tokens = math.min(capacity, tokens + (elapsed * refill_rate))

-- Try to consume
if new_tokens >= 1 then
    new_tokens = new_tokens - 1
    redis.call('HMSET', key, 'tokens', new_tokens, 'last_refill', now)
    return {1, new_tokens, capacity}  -- allowed
else
    redis.call('HMSET', key, 'tokens', new_tokens, 'last_refill', now)
    local retry_after = (1 - new_tokens) / refill_rate
    return {0, new_tokens, retry_after}  -- denied
end
```

This guarantees:
- **No race conditions** — Only one script runs per key at a time.
- **Accurate refill** — Elapsed time is calculated server-side, not affected by network latency.
- **Single round-trip** — The entire operation is one Redis call, minimizing latency.

---

## Practical Example

Consider a client with:
- **Capacity**: 100 tokens
- **Refill Rate**: 10 tokens/second

| Time | Event | Tokens | Result |
|------|-------|--------|--------|
| 0.0s | Bucket initialized | 100 | — |
| 0.0s | Burst of 50 requests | 50 | All allowed |
| 0.0s | 51st request | 49 | Allowed |
| 5.0s | No requests (refill) | 99 | — |
| 5.0s | 1 request | 98 | Allowed |
| 10.0s | No requests (refill) | 100 (capped) | — |
| 10.0s | Burst of 100 requests | 0 | All allowed |
| 10.0s | 101st request | 0 | **Denied** (Retry-After: 0.1s) |
| 10.1s | 1 request | 0 | Allowed (1 token refilled) |

This shows how Token Bucket naturally handles bursty traffic while enforcing a sustained rate of 10 requests/second.

---

## Configuration in This Project

Rate limiting rules are defined per client type in `ratelimit-rules.json`:

```json
{
  "Rules": [
    {
      "RuleId": "api-key-rule",
      "BucketCapacity": 100,
      "RefillRatePerSecond": 10,
      "AppliesTo": "ApiKey"
    },
    {
      "RuleId": "ip-address-rule",
      "BucketCapacity": 20,
      "RefillRatePerSecond": 2,
      "AppliesTo": "IpAddress"
    }
  ]
}
```

Different client types can have different budgets. An authenticated API key client might get 100 tokens with a 10/sec refill, while an anonymous IP-identified client gets a more restrictive 20 tokens at 2/sec.

---

## Further Reading

- [Token Bucket - Wikipedia](https://en.wikipedia.org/wiki/Token_bucket)
- [Rate Limiting Strategies and Techniques - Google Cloud Architecture](https://cloud.google.com/architecture/rate-limiting-strategies-techniques)
- [Redis Rate Limiting Pattern](https://redis.io/glossary/rate-limiting/)
