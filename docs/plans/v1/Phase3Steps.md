# Phase 3 Implementation Steps — SQLite Database & Request Recording

This document records every step taken to implement Phase 3 of the implementation plan.

---

## Overview

Phase 3 adds persistent storage for every proxied request/response pair. Each call is written to a SQLite database using Entity Framework Core. The recording path is fire-and-forget so it never adds latency to the response returned to the client.

---

## Step 1 — Created Entity Models (Task 3.1)

**Files created:**
- `ClaudeCodeProxy/Models/ProxyRequest.cs`
- `ClaudeCodeProxy/Models/LlmUsage.cs`

`ProxyRequest` holds one row per proxied HTTP exchange with the following columns:

| Property | Type | Notes |
|---|---|---|
| `Id` | `long` PK | Auto-increment |
| `Timestamp` | `DateTime` | UTC time the request was received |
| `Method` | `string` | HTTP method |
| `Path` | `string` | Request path + query string |
| `RequestHeaders` | `string` | JSON-serialised request headers |
| `RequestBody` | `string?` | Raw request body (null if empty) |
| `ResponseStatusCode` | `int` | HTTP status code |
| `ResponseHeaders` | `string` | JSON-serialised response headers |
| `ResponseBody` | `string?` | Raw response body (null if empty) |
| `DurationMs` | `long` | Total proxy duration in milliseconds |

`LlmUsage` holds token-usage data for Anthropic Messages API calls (populated in Phase 4) and is linked to `ProxyRequest` via a one-to-one foreign key (`ProxyRequestId`).

---

## Step 2 — Created the DbContext (Task 3.2)

**File created:** `ClaudeCodeProxy/Data/ProxyDbContext.cs`

- Defined `DbSet<ProxyRequest> ProxyRequests` and `DbSet<LlmUsage> LlmUsages`.
- Configured the one-to-one relationship between `LlmUsage` and `ProxyRequest` in `OnModelCreating`, with a unique index on `LlmUsage.ProxyRequestId` to enforce the one-to-one constraint at the database level.

---

## Step 3 — Added SQLite Connection String to appsettings.json (Task 3.2)

**File modified:** `ClaudeCodeProxy/appsettings.json`

Added the `ConnectionStrings` section:
```json
"ConnectionStrings": {
  "DefaultConnection": "Data Source=proxy.db"
}
```

The database file `proxy.db` is created in the working directory at startup.

---

## Step 4 — Extracted IRecordingService Interface (Task 3.4)

**File created:** `ClaudeCodeProxy/Services/IRecordingService.cs`

Created `IRecordingService` with a single method:
```csharp
void Record(ProxyRequest request);
```

This interface was introduced so that `ProxyMiddleware` depends on an abstraction rather than the concrete class, making it easy to mock in unit tests with Moq.

---

## Step 5 — Implemented RecordingService (Task 3.4)

**File created:** `ClaudeCodeProxy/Services/RecordingService.cs`

`RecordingService` implements `IRecordingService`. Key design decisions:

- **Singleton lifetime** — registered as a singleton so the middleware can receive it via constructor injection without scope issues.
- **Fire-and-forget via `Task.Run`** — `Record(ProxyRequest)` enqueues the database write on a background thread and returns immediately. This means the response is never blocked waiting for the DB write.
- **`IServiceScopeFactory` for scoped DbContext** — because `ProxyDbContext` is registered with a scoped lifetime and `RecordingService` is a singleton, it cannot directly hold a reference to the DbContext. Instead it creates a new DI scope for each background write.
- **Non-fatal error handling** — any exception from the database write is caught and logged as a warning; it is never rethrown so recording failures do not affect proxy clients.

---

## Step 6 — Registered DbContext and RecordingService in Program.cs (Task 3.2 / Task 3.4)

**File modified:** `ClaudeCodeProxy/Program.cs`

Added:
```csharp
builder.Services.AddDbContext<ProxyDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<IRecordingService, RecordingService>();
```

Also added automatic migration-on-startup so that the SQLite schema is always up to date without manual intervention:
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
    db.Database.Migrate();
}
```

---

## Step 7 — Wired RecordingService into ProxyMiddleware (Task 3.5)

**File modified:** `ClaudeCodeProxy/Middleware/ProxyMiddleware.cs`

Changes made to the middleware:

1. **Added `IRecordingService` constructor parameter** — the middleware now receives the recording service via DI.
2. **Captured `Timestamp` at the start of the request** — recorded as `DateTime.UtcNow` before any async work.
3. **Serialised request headers to JSON** — at the top of `InvokeAsync`, before the body is forwarded, request headers are captured with `JsonSerializer.Serialize`.
4. **Serialised response headers to JSON** — after the upstream response headers arrive, both main headers and content headers are merged and serialised.
5. **Read both body streams after the response is sent** — after `sw.Stop()`, both `requestBodyMs` and `responseBodyMs` are reset to position 0 and read as UTF-8 strings for recording.
6. **Built and dispatched a `ProxyRequest` record** — populated all fields and called `_recordingService.Record(record)`.
7. **Early-return paths are not recorded** — timeout (504) and connection failure (502) paths return early before reaching the `Record` call, which is correct since no proxied response was returned.

---

## Step 8 — Created and Applied EF Core Migration (Task 3.3)

Installed the `dotnet-ef` global tool (was not present in the dev environment):
```bash
dotnet tool install --global dotnet-ef
```

Generated the initial migration:
```bash
dotnet ef migrations add InitialCreate --project ClaudeCodeProxy
```

This created `ClaudeCodeProxy/Data/Migrations/` with `InitialCreate` migration files.

Applied the migration to create the SQLite database:
```bash
dotnet ef database update --project ClaudeCodeProxy
```

Tables created:
- `ProxyRequests` with all columns as defined in the entity model
- `LlmUsages` with a foreign key to `ProxyRequests` and a unique index on `ProxyRequestId`

---

## Step 9 — Added Moq and Updated Tests

**Test project file modified:** `ClaudeCodeProxy.Tests/ClaudeCodeProxy.Tests.csproj`

Added the Moq NuGet package:
```bash
dotnet add ClaudeCodeProxy.Tests/ClaudeCodeProxy.Tests.csproj package Moq
```

**Test file modified:** `ClaudeCodeProxy.Tests/Middleware/ProxyMiddlewareTests.cs`

- Updated `CreateMiddleware` helper to pass a `Mock<IRecordingService>` no-op instance so all existing tests compile without change.
- Added `CreateMiddlewareWithRecordingMock` helper that also returns the mock so recording-specific tests can run assertions on it.
- Added three new recording tests:
  - **`RecordingService_IsCalledAfterSuccessfulRequest`** — verifies that `Record` is called once with the correct method, path, status code, response body, and a non-negative duration.
  - **`RecordingService_IsNotCalledOnUpstreamConnectionFailure`** — verifies that `Record` is never called when the upstream throws an `HttpRequestException`.
  - **`RecordingService_CapturesRequestBody`** — verifies that the request body text is correctly passed through to the `ProxyRequest` record.

---

## Final State

- **12 tests pass** (`dotnet test`)
- **Solution builds with 0 errors and 0 warnings** (`dotnet build ClaudeCodeProxyDotNet.slnx`)
- **SQLite database `proxy.db`** is created on first run with the correct schema
- Every successful proxy request is persisted to the `ProxyRequests` table asynchronously, without blocking the client response
