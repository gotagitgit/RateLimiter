# RateLimiter

A .NET 10 rate-limiting reverse proxy gateway that protects backend APIs from excessive traffic using the **Token Bucket algorithm**. Incoming requests pass through a rate-limiting middleware before being forwarded to downstream services via [YARP (Yet Another Reverse Proxy)](https://github.com/microsoft/reverse-proxy). When a client exceeds its allowed token budget, the gateway denies the request with HTTP 429 (Too Many Requests).

---

## How It Works

```
                         ┌─────────────────────────────────────────────────────┐
                         │              RateLimiter Gateway                    │
                         │                                                     │
                         │  ┌───────────────┐      ┌──────────────────────┐    │
   ┌────────┐            │  │  Rate Limit   │      │   YARP Reverse Proxy │    │     ┌────────────┐
   │ Client ├───Request──┼─►│  Middleware   ├─────►│   (Forward Request)  ├─── ┼────►│ Sample API │
   └────────┘            │  └───────┬───────┘      └──────────────────────┘    │     └────────────┘
                         │          │                                          │
                         │          │ Tokens                                   │
                         │          │ Available?                               │
                         │          │                                          │
                         │     ┌────▼────┐                                     │
                         │     │  Redis  │                                     │
                         │     │ (State) │                                     │
                         │     └─────────┘                                     │
                         │                                                     │
                         └─────────────────────────────────────────────────────┘

   ┌────────┐            ┌───────────────┐
   │ Client ├───Request─►│  Rate Limit   ├──── Tokens Exhausted ──► HTTP 429 (Too Many Requests)
   └────────┘            │  Middleware   │       + Retry-After header
                         └───────────────┘
```

### Request Flow

1. A client sends a request to the gateway.
2. The **Rate Limit Middleware** identifies the client (by API key, user ID, or IP address).
3. The middleware checks the client's token bucket in **Redis** using an atomic Lua script.
4. **If tokens are available**: one token is consumed and the request is forwarded through YARP to the backend API.
5. **If tokens are exhausted**: the request is denied with HTTP 429, and a `Retry-After` header tells the client when to try again.

---

## Token Bucket Algorithm

The Token Bucket is a rate-limiting algorithm that controls throughput by maintaining a virtual "bucket" of tokens for each client.

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

    How it works:

    1. INITIALIZE
       Bucket starts full (capacity tokens).

    2. REQUEST ARRIVES
       ├── Tokens ≥ 1?
       │     ├── YES → Consume 1 token, ALLOW request
       │     └── NO  → DENY request (HTTP 429)
       │               Return Retry-After = (1 - tokens) / refill_rate

    3. REFILL (continuous)
       Tokens accumulate at refill_rate per second,
       capped at bucket capacity.

       elapsed_ms × refill_rate / 1000 = tokens_to_add
```

### Key Properties

| Property | Description |
|----------|-------------|
| **Bucket Capacity** | Maximum number of tokens (burst limit) |
| **Refill Rate** | Tokens added per second (sustained throughput) |
| **Burst Tolerance** | A full bucket allows short bursts up to capacity |
| **Smooth Refill** | Tokens refill continuously, not in fixed windows |

### Why Token Bucket?

- **Allows bursts**: Unlike fixed-window counters, clients can burst up to the bucket capacity.
- **Smooth rate limiting**: Tokens refill continuously, so there's no "reset cliff" at window boundaries.
- **Predictable**: Clients know exactly when they can retry via the `Retry-After` header.
- **Atomic in Redis**: The entire check-and-decrement runs as a single Lua script, preventing race conditions across multiple gateway instances.

---

## YARP Reverse Proxy

Requests that pass rate limiting are forwarded to backend services using [YARP](https://github.com/microsoft/reverse-proxy). The gateway acts as a transparent reverse proxy — clients interact with a single endpoint, and YARP routes traffic to the appropriate downstream service.

### Sample API

The project includes a **Sample API** (`RateLimiter.SampleApi`) that serves as the backend target. All requests matching `/api/{**catch-all}` are proxied through to this service.

```json
"ReverseProxy": {
  "Routes": {
    "sampleapi-route": {
      "ClusterId": "sampleapi-cluster",
      "Match": { "Path": "/api/{**catch-all}" }
    }
  },
  "Clusters": {
    "sampleapi-cluster": {
      "Destinations": {
        "destination1": { "Address": "http://sampleapi" }
      }
    }
  }
}
```

The Sample API exposes a simple products endpoint:

```
GET /api/products → [{ id: 1, name: "Widget", price: 9.99 }, ...]
```

You can send requests to the gateway at `/api/products` and they will be rate-limited, then forwarded to the Sample API if allowed.

---

## Project Structure

```
RateLimiter/
├── src/
│   ├── RateLimiter.Domain           # Core domain: TokenBucketAlgorithm, rules, decisions
│   ├── RateLimiter.Application      # Application services: RateLimitService, rule provider
│   ├── RateLimiter.Infrastructure   # Redis state store (Lua scripts), resilience, config polling
│   ├── RateLimiter.Gateway          # ASP.NET Core host: middleware + YARP reverse proxy
│   ├── RateLimiter.SampleApi        # Backend API for testing (products endpoint)
│   ├── RateLimiter.AppHost          # .NET Aspire orchestrator
│   └── RateLimiter.ServiceDefaults  # Shared Aspire defaults (OpenTelemetry, health checks)
├── tests/
│   ├── RateLimiter.Domain.Tests
│   ├── RateLimiter.Application.Tests
│   ├── RateLimiter.Infrastructure.Tests
│   ├── RateLimiter.Gateway.Tests
│   └── RateLimiter.Gateway.IntegrationTests
└── RateLimiter.slnx
```

---

## Features

- **Token Bucket rate limiting** with configurable capacity and refill rate per client type
- **Client identification** by API key, user ID, or IP address (priority configurable)
- **Redis-backed state** with atomic Lua scripts for consistency across multiple gateway instances
- **YARP reverse proxy** forwarding allowed requests to downstream services
- **Fail-open / fail-close policies** when Redis is unavailable
- **Polly resilience** (timeout + circuit breaker) around Redis operations
- **Dynamic rule configuration** via JSON file with automatic polling (30s interval)
- **.NET Aspire** orchestration with service discovery
- **OpenTelemetry** observability (traces, metrics, logs)
- **OpenAPI / Swagger UI** in development mode

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Redis](https://redis.io/) (local instance or container)
- [.NET Aspire workload](https://learn.microsoft.com/en-us/dotnet/aspire/) (optional, for orchestrated run)

### Run with Aspire

```bash
dotnet run --project src/RateLimiter.AppHost
```

This starts both the gateway and the sample API with service discovery.

### Run Standalone

```bash
# Start Redis
docker run -d -p 6379:6379 redis

# Start the Sample API
dotnet run --project src/RateLimiter.SampleApi

# Start the Gateway
dotnet run --project src/RateLimiter.Gateway
```

### Test It

```bash
# Send a request through the gateway (proxied to Sample API)
curl http://localhost:5000/api/products

# Response headers include rate limit info:
# X-Rate-Limit-Limit: 100
# X-Rate-Limit-Remaining: 99
```

---

## Configuration

Rate limiting rules are configured in `ratelimit-rules.json` and polled every 30 seconds:

```json
{
  "Rules": [
    {
      "RuleId": "api-key-rule",
      "BucketCapacity": 100,
      "RefillRatePerSecond": 10,
      "AppliesTo": "ApiKey"
    }
  ]
}
```

Gateway settings in `appsettings.json`:

| Setting | Description | Default |
|---------|-------------|---------|
| `RateLimit:FailurePolicy` | `FailOpen` or `FailClose` when Redis is down | `FailClose` |
| `RateLimit:TimeoutMs` | Redis operation timeout | `50` |
| `RateLimit:HealthCheckIntervalSeconds` | Circuit breaker recovery interval | `5` |
| `RateLimit:Middleware:IdentificationPriority` | Client ID strategy order | `[ApiKey, UserId, IpAddress]` |

---

## Tech Stack

- **.NET 10** / ASP.NET Core
- **YARP 2.3** — Reverse proxy
- **StackExchange.Redis** — Redis client
- **Polly** — Resilience (timeout, circuit breaker)
- **.NET Aspire 9.3** — Orchestration and service discovery
- **OpenTelemetry** — Observability
- **xUnit + FsCheck** — Unit and property-based testing
- **Testcontainers** — Integration tests with real Redis
- **WireMock.Net** — HTTP mocking for integration tests
