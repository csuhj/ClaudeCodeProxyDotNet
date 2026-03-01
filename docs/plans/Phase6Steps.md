# Phase 6 Implementation Steps

## Overview

Phase 6 adds an analytics API to the proxy, exposing two endpoints that return aggregated request and LLM token-usage statistics bucketed by hour or day. The implementation follows the plan defined in `ImplementationPlan-v1.md` (Tasks 6.1 – 6.3).

---

## Step 1 — Create the `StatsBucket` DTO model

**File created:** `src/ClaudeCodeProxy/Models/StatsBucket.cs`

Defines the shape of a single aggregated time bucket returned by both stats endpoints:

| Property | Type | Description |
|---|---|---|
| `TimeBucket` | `DateTime` | Start of the bucket (UTC), truncated to hour or day |
| `RequestCount` | `int` | Total proxied requests in this bucket |
| `LlmRequestCount` | `int` | Requests that had a linked `LlmUsage` row |
| `TotalInputTokens` | `long` | Sum of input tokens across LLM calls |
| `TotalOutputTokens` | `long` | Sum of output tokens across LLM calls |

---

## Step 2 — Create `IStatsService` interface

**File created:** `src/ClaudeCodeProxy/Services/IStatsService.cs`

Defines two async query methods:
- `GetRequestsPerHourAsync(DateTime from, DateTime to) → List<StatsBucket>`
- `GetRequestsPerDayAsync(DateTime from, DateTime to) → List<StatsBucket>`

Both methods treat the `to` bound as exclusive (i.e. `Timestamp >= from && Timestamp < to`).

---

## Step 3 — Implement `StatsService`

**File created:** `src/ClaudeCodeProxy/Services/StatsService.cs`

Key design decisions:

- **EF Core navigation property for LEFT JOIN.** Accessing `r.LlmUsage` inside a `.Select()` projection causes EF Core to generate a `LEFT JOIN` against `LlmUsages`. This gives us `HasLlm`, `InputTokens`, and `OutputTokens` per row without an explicit `join … DefaultIfEmpty()` expression.

- **In-memory grouping.** Date-truncation logic (truncating to the hour or to midnight) is performed in .NET LINQ after the database query rather than in SQL. This avoids reliance on SQLite-specific date functions (`strftime`, etc.) and keeps the query portable.

- **Private `RequestProjection` class.** A sealed nested record holds only the three fields needed for grouping (`Timestamp`, `HasLlm`, `InputTokens`, `OutputTokens`). This minimises the data transferred from the database and keeps the projection anonymous-type-free for clarity.

- **Single shared fetch helper.** `FetchProjectedDataAsync` is called by both `GetRequestsPerHourAsync` and `GetRequestsPerDayAsync` to avoid code duplication; the two methods differ only in how they group the in-memory results.

---

## Step 4 — Create `StatsController`

**File created:** `src/ClaudeCodeProxy/Controllers/StatsController.cs`

- Decorated with `[ApiController]` and `[Route("api/stats")]`.
- Two action methods:
  - `GET /api/stats/hourly?from=...&to=...`
  - `GET /api/stats/daily?from=...&to=...`
- Both `from` and `to` are optional `DateTime?` query parameters. When omitted, `to` defaults to `DateTime.UtcNow` and `from` defaults to 7 days before `to`.
- Both parameters are normalised to UTC via `.ToUniversalTime()` before being passed to the service.

---

## Step 5 — Update `Program.cs`

**File modified:** `src/ClaudeCodeProxy/Program.cs`

Two additions to the service registration block:

```csharp
builder.Services.AddScoped<IStatsService, StatsService>();
builder.Services.AddControllers();
```

`IStatsService`/`StatsService` is registered as **scoped** because `StatsService` depends directly on `ProxyDbContext`, which is also scoped.

One addition to the app pipeline, placed **before** `app.UseMiddleware<ProxyMiddleware>()`:

```csharp
app.MapControllers();
```

Placing `MapControllers()` first registers the controller endpoints in ASP.NET Core's endpoint routing table. The routing middleware (implicit in `WebApplication`) then selects those endpoints for matching requests before `ProxyMiddleware` runs. `ProxyMiddleware` checks `context.GetEndpoint()` (Step 6) and yields to them.

---

## Step 6 — Update `ProxyMiddleware` to yield to matched endpoints

**File modified:** `src/ClaudeCodeProxy/Middleware/ProxyMiddleware.cs`

Two changes:

1. **Store `_next`** — the constructor previously discarded the `RequestDelegate` parameter with `RequestDelegate _`. It now stores it as `private readonly RequestDelegate _next`.

2. **Endpoint guard at the top of `InvokeAsync`** — before any proxy logic runs, the middleware checks whether endpoint routing has already selected an endpoint:

```csharp
if (context.GetEndpoint() != null)
{
    await _next(context);
    return;
}
```

If an endpoint is matched (e.g. a controller action for `/api/stats/hourly`), control is passed to the next middleware in the pipeline (which will ultimately execute the endpoint). The proxy logic is skipped entirely for those requests.

---

## Step 7 — Unit tests for `StatsService` (Task 6.3)

**File created:** `test/ClaudeCodeProxy.Tests/Services/StatsServiceTests.cs`

Uses the same in-memory SQLite pattern as `RecordingRepositoryTests` (a `SqliteConnection` kept open for the lifetime of each test and a `ProxyDbContext` created against it with `EnsureCreated()`).

### Tests for `GetRequestsPerHour`:

| Test | Verifies |
|---|---|
| `ReturnsEmpty_WhenNoDataInRange` | Empty result when the database has no rows in the window |
| `ExcludesRequestsOutsideDateRange` | `from` is inclusive, `to` is exclusive |
| `GroupsRequestsIntoCorrectHourBuckets` | Requests at :00 and :45 of the same hour land in one bucket; requests in the next hour land in a separate bucket |
| `TimeBucketIsTruncatedToHour` | Minutes and seconds are zeroed; the bucket DateTime reflects the start of the hour |
| `CountsLlmRequestsCorrectly` | Requests with a linked `LlmUsage` are counted separately from total requests |
| `SumsTokensCorrectly` | `TotalInputTokens` and `TotalOutputTokens` are the correct sums; non-LLM requests contribute 0 |
| `ReturnsBucketsInAscendingOrder` | Buckets are sorted chronologically regardless of insertion order |

### Tests for `GetRequestsPerDay`:

| Test | Verifies |
|---|---|
| `ReturnsEmpty_WhenNoDataInRange` | Empty result when the database has no rows in the window |
| `GroupsRequestsByDay` | Requests on different days produce separate buckets with correct counts |
| `TimeBucketIsTruncatedToMidnight` | Time-of-day is zeroed; bucket is midnight UTC |
| `SumsTokensAcrossMultipleDayRequests` | All fields aggregated correctly across multiple LLM and non-LLM requests in a day |
| `ReturnsBucketsInAscendingOrder` | Buckets are sorted chronologically regardless of insertion order |

---

## Verification

```
dotnet build ClaudeCodeProxyDotNet.slnx   # 0 warnings, 0 errors
dotnet test                                 # 75/75 passed
```

All 75 tests pass, including 11 new `StatsServiceTests`.
