# Phase 5 Steps — Configuration, Logging & Hardening

This document records every step taken to implement Phase 5 of the implementation plan.

---

## Pre-work: Remove Task 5.3

Before starting Phase 5, Task 5.3 (Environment Variable Support for `ANTHROPIC_BASE_URL`) was
removed from the implementation plan.  The decision not to use the `ANTHROPIC_BASE_URL`
environment variable to configure the proxy was made during code reviews after Phase 3 — the
proxy's upstream URL is intentionally configured via `Upstream:BaseUrl` in `appsettings.json`
only, keeping the configuration surface simple and explicit.

**File edited:** `docs/plans/ImplementationPlan-v1.md`
**Change:** Deleted the Task 5.3 section entirely.

---

## Task 5.1 — Structured Logging

### What was already in place

The middleware already used ASP.NET Core's built-in `ILogger<T>` with named structured
parameters (`{Method}`, `{Path}`, `{StatusCode}`, etc.).  Five log points existed across
`ProxyMiddleware` and `RecordingService`.

### Changes made

#### 1. Request-scoped logging context (`ProxyMiddleware.cs`)

Added a `_logger.BeginScope(...)` call at the top of `InvokeAsync` that attaches two structured
fields — `RequestMethod` and `RequestPath` — to every log statement emitted during that
request's lifetime.  This means log aggregation tools can filter or correlate all log lines
for a single request even when the full message text differs.

```csharp
using var logScope = _logger.BeginScope(
    new Dictionary<string, object>
    {
        ["RequestMethod"] = context.Request.Method,
        ["RequestPath"] = pathAndQuery
    });
```

#### 2. Debug-level "Request received" log (`ProxyMiddleware.cs`)

Added a `LogDebug` statement immediately after the scope is opened so that incoming requests
are visible when the log level is set to `Debug`:

```csharp
_logger.LogDebug("Request received: {Method} {Path}", context.Request.Method, pathAndQuery);
```

This complements the existing `LogInformation` line that fires after the response is complete
— together they let operators see both request arrival and completion.

#### 3. `appsettings.Development.json` — Debug level and `IncludeScopes`

Updated the development configuration so that:
- The default log level in development is `Debug` (allows the new "Request received" log to
  appear without changing production settings).
- `Console.IncludeScopes: true` is set so the console logger renders the scope properties
  (RequestMethod, RequestPath) alongside each log line.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Warning"
    },
    "Console": {
      "IncludeScopes": true
    }
  }
}
```

Production `appsettings.json` remains at `"Default": "Information"` so debug noise is
suppressed in production.

#### 4. Path variable in the completion log (`ProxyMiddleware.cs`)

The existing `LogInformation` completion line was updated to use the pre-computed `pathAndQuery`
variable (which includes the query string) instead of `context.Request.Path.Value`.  This
keeps the logged path consistent with what was scoped and what is stored in the database.

---

## Task 5.2 — Error Handling & Edge Cases

### Existing coverage

The following edge cases were already handled from Phase 2:
- **502 Bad Gateway** when the upstream connection fails (`HttpRequestException`)
- **504 Gateway Timeout** when the upstream times out (`TaskCanceledException`)
- **Client disconnection mid-stream** (graceful `OperationCanceledException` handling in Step 5)
- **Recording failures** logged as warnings, never propagated to the client

### New: Body-size cap for stored bodies

Very large request or response bodies were previously stored in full in the SQLite database,
which could cause excessive disk usage for large LLM payloads.

#### Configuration — `UpstreamOptions.cs`

Added a new `MaxStoredBodyBytes` property (default **1 048 576 bytes = 1 MiB**):

```csharp
/// <summary>
/// Maximum number of bytes stored for each request or response body in the
/// database.  Bodies that exceed this limit are truncated and a note is
/// appended so the truncation is visible in recorded data.
/// Default is 1 048 576 (1 MiB).
/// </summary>
public int MaxStoredBodyBytes { get; set; } = 1_048_576;
```

#### Configuration — `appsettings.json`

The value is exposed in `appsettings.json` so operators can override it without recompiling:

```json
"Upstream": {
  "BaseUrl": "https://api.anthropic.com",
  "TimeoutSeconds": 300,
  "MaxStoredBodyBytes": 1048576
}
```

#### `TruncateForStorage` helper (`ProxyMiddleware.cs`)

A new `internal static` helper method performs the truncation:

```csharp
internal static string? TruncateForStorage(string? body, int maxBytes)
{
    if (body == null) return null;
    var encoded = Encoding.UTF8.GetBytes(body);
    if (encoded.Length <= maxBytes) return body;
    var truncated = Encoding.UTF8.GetString(encoded, 0, maxBytes);
    return truncated + $"\n[TRUNCATED: original size was {encoded.Length} bytes, stored first {maxBytes} bytes]";
}
```

Key design decisions:
- Operates on UTF-8 byte count (not character count) so multi-byte characters are handled safely.
- The `GetString` call after byte-slicing automatically rounds down to the nearest valid character boundary.
- Returns `null` unchanged so callers don't need a null-check.
- Appends a human-readable note so the truncation is discoverable when inspecting the database.
- Marked `internal` so it can be unit-tested directly without going through the full middleware pipeline.

#### Applied in `RecordRequestAsync` (`ProxyMiddleware.cs`)

Both the request body and decoded response body now pass through `TruncateForStorage` before
being placed in the `ProxyRequest` record:

```csharp
var requestBodyText = requestBodyMs.Length > 0
    ? TruncateForStorage(await ReadAsStringAsync(requestBodyMs), _maxStoredBodyBytes)
    : null;

var responseBodyText = TruncateForStorage(responseBodyRaw, _maxStoredBodyBytes);
```

Debug logs are also emitted when either body is actually truncated, showing the original and
stored byte counts.

---

## Unit Tests

Eight new tests were added to
`test/ClaudeCodeProxy.Tests/Middleware/ProxyMiddlewareTests.cs`.

### `TruncateForStorage` unit tests (4 tests)

| Test | Assertion |
|------|-----------|
| `TruncateForStorage_NullBody_ReturnsNull` | `null` input returns `null` |
| `TruncateForStorage_BodyWithinLimit_ReturnsUnchanged` | Body ≤ limit is returned verbatim |
| `TruncateForStorage_BodyExceedsLimit_ReturnsTruncatedWithNote` | Truncated body starts with first N bytes and contains `[TRUNCATED:]` with original and stored byte counts |
| `TruncateForStorage_EmptyBody_ReturnsEmpty` | Empty string passes through unchanged |

### Body-truncation integration tests (3 tests)

| Test | Assertion |
|------|-----------|
| `RecordingService_ResponseBodyExceedsLimit_ReceivesTruncatedBody` | Recording service receives truncated response body with `[TRUNCATED:]` note when limit is 50 bytes and body is 200 bytes |
| `RecordingService_RequestBodyExceedsLimit_ReceivesTruncatedBody` | Recording service receives truncated request body with `[TRUNCATED:]` note under the same conditions |
| `RecordingService_BodyWithinLimit_IsNotTruncated` | Recording service receives the full, unmodified body when body size is within the default 1 MiB limit |

### Test infrastructure change

The `CreateMiddlewareCore` helper was given an overload that accepts a custom `UpstreamOptions`
instance (in addition to the existing zero-arg factory that uses defaults).  This lets the
new tests set a small `MaxStoredBodyBytes` (e.g. 50 bytes) without needing enormous fixtures.

---

## Files Changed

| File | Change |
|------|--------|
| `docs/plans/ImplementationPlan-v1.md` | Removed Task 5.3 |
| `src/ClaudeCodeProxy/Models/UpstreamOptions.cs` | Added `MaxStoredBodyBytes` property (default 1 MiB) |
| `src/ClaudeCodeProxy/appsettings.json` | Added `Upstream.MaxStoredBodyBytes: 1048576` |
| `src/ClaudeCodeProxy/appsettings.Development.json` | Set log level to `Debug`; enabled `Console.IncludeScopes` |
| `src/ClaudeCodeProxy/Middleware/ProxyMiddleware.cs` | Added `_maxStoredBodyBytes` field; request-scope logging; Debug "Request received" log; `TruncateForStorage` static helper; body truncation applied in `RecordRequestAsync` |
| `test/ClaudeCodeProxy.Tests/Middleware/ProxyMiddlewareTests.cs` | Added `CreateMiddlewareCore` overload; 8 new tests for truncation |

---

## Test Results

All **61 tests** pass after Phase 5 changes (53 pre-existing + 8 new).

```
Passed!  - Failed: 0, Passed: 61, Skipped: 0, Total: 61
```
