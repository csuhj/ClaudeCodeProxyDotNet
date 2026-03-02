# V2 Implementation Steps — Phases 1 to 5

## Phase 1: Backend — Data Transfer Objects

### Step 1.1 — Created `LlmRequestSummary` DTO

**File created:** `src/ClaudeCodeProxy/Models/LlmRequestSummary.cs`

A lightweight DTO for the list view — contains metadata fields only, no request or response body content. Token fields (`Model`, `InputTokens`, `OutputTokens`, `CacheReadTokens`, `CacheCreationTokens`) are nullable `int?`/`string?` because they come from the optionally-joined `LlmUsage` record.

Fields:
- `Id` (long)
- `Timestamp` (DateTime)
- `Method` (string)
- `Path` (string)
- `ResponseStatusCode` (int)
- `DurationMs` (long)
- `Model` (string?)
- `InputTokens` (int?)
- `OutputTokens` (int?)
- `CacheReadTokens` (int?)
- `CacheCreationTokens` (int?)

### Step 1.2 — Created `LlmRequestDetail` DTO

**File created:** `src/ClaudeCodeProxy/Models/LlmRequestDetail.cs`

Inherits from `LlmRequestSummary` and adds the full body and header fields needed for the drill-down view. Includes a derived `IsStreaming` boolean flag so the Angular frontend knows which rendering path to use without having to parse the headers itself.

Additional fields (beyond `LlmRequestSummary`):
- `RequestHeaders` (string) — JSON-serialised header dictionary
- `RequestBody` (string?)
- `ResponseHeaders` (string) — JSON-serialised header dictionary
- `ResponseBody` (string?)
- `IsStreaming` (bool) — derived from `Content-Type: text/event-stream` in stored response headers

---

## Phase 2: Backend — Repository Extension

### Step 2.1 — Added `GetLlmRequestsAsync` to `IRecordingRepository`

**File modified:** `src/ClaudeCodeProxy/Data/IRecordingRepository.cs`

Added new method signature with XML doc comment:

```csharp
Task<List<LlmRequestSummary>> GetLlmRequestsAsync(
    DateTime from, DateTime to, int skip, int take, CancellationToken ct = default);
```

### Step 2.2 — Added `GetLlmRequestByIdAsync` to `IRecordingRepository`

**File modified:** `src/ClaudeCodeProxy/Data/IRecordingRepository.cs`

Added new method signature with XML doc comment:

```csharp
Task<LlmRequestDetail?> GetLlmRequestByIdAsync(long id, CancellationToken ct = default);
```

### Step 2.3 — Implemented both methods in `RecordingRepository`

**File modified:** `src/ClaudeCodeProxy/Data/RecordingRepository.cs`

Added `using System.Text.Json;` import at the top.

**`GetLlmRequestsAsync`** implementation:
- Filters `ProxyRequests` to the `[from, to)` time window using `.Where(r => r.Timestamp >= from && r.Timestamp < to && r.LlmUsage != null)`
- Orders by `Timestamp` descending (newest first)
- Applies `.Skip(skip).Take(take)` for pagination
- Projects directly to `LlmRequestSummary` using EF Core's `.Select(...)` to avoid loading body columns from the database

**`GetLlmRequestByIdAsync`** implementation:
- Loads the `ProxyRequest` row by id using `.Include(r => r.LlmUsage).SingleOrDefaultAsync(...)`
- Returns `null` if not found
- Maps to `LlmRequestDetail` in-memory (C#, not SQL), including calling the private helper for `IsStreaming`

**`IsStreamingResponse` private static helper:**
- Deserialises the `ResponseHeaders` JSON string using `System.Text.Json`
- Iterates keys looking for `Content-Type` (case-insensitive)
- Returns `true` if the value contains `text/event-stream` (case-insensitive)
- Catches all exceptions and returns `false` (defensive — stored JSON should always be valid)

---

## Phase 3: Backend — Service

### Step 3.1 — Created `IRequestsService` interface

**File created:** `src/ClaudeCodeProxy/Services/IRequestsService.cs`

Follows the same style as `IStatsService`. Two methods:

```csharp
Task<List<LlmRequestSummary>> GetRecentLlmRequestsAsync(
    DateTime from, DateTime to, int page, int pageSize, CancellationToken ct = default);

Task<LlmRequestDetail?> GetLlmRequestDetailAsync(long id, CancellationToken ct = default);
```

### Step 3.2 — Created `RequestsService` implementation

**File created:** `src/ClaudeCodeProxy/Services/RequestsService.cs`

- Declares `private const int MaxPageSize = 200`
- `GetRecentLlmRequestsAsync`: clamps `pageSize` into `[1, 200]` using `Math.Clamp`, calculates `skip = page * clampedPageSize`, delegates to `IRecordingRepository.GetLlmRequestsAsync`
- `GetLlmRequestDetailAsync`: delegates directly to `IRecordingRepository.GetLlmRequestByIdAsync`

**File modified:** `src/ClaudeCodeProxy/Program.cs`

Added one line alongside the existing `IStatsService` registration:

```csharp
builder.Services.AddScoped<IRequestsService, RequestsService>();
```

---

## Phase 4: Backend — Controller

### Step 4.1 — Created `RequestsController`

**File created:** `src/ClaudeCodeProxy/Controllers/RequestsController.cs`

Route: `[ApiController] [Route("api/requests")]` — follows the same structure as `StatsController`.

**`GET /api/requests`**
- Query params: `from` (DateTime?), `to` (DateTime?), `page` (int, default 0), `pageSize` (int, default 50)
- Defaults `to` to `DateTime.UtcNow` and `from` to 24 hours before `to` when omitted, satisfying Requirement 7 with no parameters required
- Calls `.ToUniversalTime()` on both dates to handle any timezone offset in query params
- Returns `200 OK` with `List<LlmRequestSummary>`

**`GET /api/requests/{id:long}`**
- Path param: `id` (long)
- Returns `200 OK` with `LlmRequestDetail` if found
- Returns `404 Not Found` if `IRequestsService.GetLlmRequestDetailAsync` returns `null`

### Step 4.2 — Service registration (done in Phase 3)

`IRequestsService` / `RequestsService` was already registered as scoped in `Program.cs` during Step 3.2. No further changes were needed — ASP.NET Core's controller discovery picks up `RequestsController` automatically via `app.MapControllers()`.

---

## Phase 5: Backend — Tests

### Step 5.1 — Repository tests

**File created:** `test/ClaudeCodeProxy.Tests/Data/RecordingRepositoryLlmRequestTests.cs`

Uses the same in-memory SQLite pattern as the existing `RecordingRepositoryTests` (`SqliteConnection("Data Source=:memory:")` + `EnsureCreated()`).

Helper methods:
- `MakeRequest(timestamp, responseHeaders, usage)` — builds a `ProxyRequest` with sensible defaults
- `MakeUsage(timestamp)` — builds an `LlmUsage` with `claude-sonnet-4-6`, 100 input, 50 output tokens
- `SeedAsync(params ProxyRequest[])` — calls `_sut.AddAsync` for each

Tests for `GetLlmRequestsAsync`:
- **`ReturnsOnlyRequestsWithLlmUsage`** — seeds one LLM and one non-LLM request, asserts only 1 returned
- **`RespectsHalfOpenTimeWindow`** — seeds requests before, inside, and exactly at `to` (exclusive boundary); asserts only the in-range one is returned
- **`ReturnsResultsNewestFirst`** — seeds 3 requests at different hours, asserts descending timestamp order
- **`PaginationSkipsAndTakesCorrectly`** — seeds 5 requests (hours 0–4), calls with `skip=2, take=2`, asserts hours 2 and 1 are returned (descending positions 2–3)

Tests for `GetLlmRequestByIdAsync`:
- **`ReturnsNull_ForUnknownId`** — calls with id 99999, asserts null
- **`ReturnsAllFields_ForKnownId`** — seeds a request with known body/headers, asserts all DTO fields match
- **`IsStreamingTrue_WhenContentTypeIsEventStream`** — seeds with `{"Content-Type":"text/event-stream"}`, asserts `IsStreaming == true`
- **`IsStreamingFalse_WhenContentTypeIsApplicationJson`** — seeds with `{"Content-Type":"application/json"}`, asserts `IsStreaming == false`

### Step 5.2 — Service tests

**File created:** `test/ClaudeCodeProxy.Tests/Services/RequestsServiceTests.cs`

Uses Moq to mock `IRecordingRepository` — no database needed since `RequestsService` only delegates and applies clamping logic.

Tests:
- **`ClampsPageSizeAbove200`** — calls with `pageSize=500`, verifies repository receives `take=200`
- **`ClampsPageSizeBelow1`** — calls with `pageSize=0`, verifies repository receives `take=1`
- **`CalculatesSkipFromPageAndPageSize`** — calls with `page=3, pageSize=10`, verifies repository receives `skip=30`
- **`ValidPageSize_PassesThroughUnchanged`** — calls with `page=1, pageSize=50`, verifies repository receives `skip=50, take=50`
- **`GetLlmRequestDetailAsync_DelegatesDirectlyToRepository`** — mocks repo to return a specific detail object, asserts service returns the same reference
- **`GetLlmRequestDetailAsync_ReturnsNull_WhenRepositoryReturnsNull`** — mocks repo to return null, asserts service returns null

### Step 5.3 — Controller integration tests

**Directory created:** `test/ClaudeCodeProxy.Tests/Controllers/`

**File created:** `test/ClaudeCodeProxy.Tests/Controllers/RequestsControllerTests.cs`

Uses `WebApplicationFactory<Program>` with the same in-process hosting pattern as `EndToEndTests`. Each test gets its own named in-memory SQLite database (unique `Guid` in the name) with a keep-alive `SqliteConnection` to ensure it persists for the test lifetime.

Data is seeded by resolving `ProxyDbContext` from a factory service scope and saving directly, bypassing the proxy pipeline.

Tests:
- **`GetRequests_Returns200WithEmptyArray_WhenNoData`** — calls `GET /api/requests`, asserts `200` and body `[]`
- **`GetRequests_Returns200WithLlmRequests_WhenDataExists`** — seeds one LLM request, asserts array length 1
- **`GetRequests_ExcludesNonLlmRequests`** — seeds a request with no `LlmUsage`, asserts body is `[]`
- **`GetRequestById_Returns404_ForUnknownId`** — calls `GET /api/requests/99999`, asserts `404`
- **`GetRequestById_Returns200WithDetailDto_ForKnownId`** — seeds a request, calls `GET /api/requests/{id}`, parses the JSON response with `JsonDocument` and asserts `id`, `method`, `path`, `model`, `inputTokens`, `outputTokens`, `isStreaming`, and `requestBody` fields are present and correct (note: ASP.NET Core serialises DTO property names as camelCase in the response)

### Build and test result

All 94 tests pass (`dotnet test`) with 0 build warnings and 0 errors across both the main project and the test project.
