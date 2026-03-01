# Implementation Plan — Version 1

## Overview

This document breaks the Version 1 requirements into discrete, actionable tasks organized by phase. Each task is scoped to be independently completable and reviewable.

---

## Phase 1: Project Scaffold & Solution Setup

### Task 1.1 — Create the .NET Solution and Project
- Create a new ASP.NET Core Web API project: `ClaudeCodeProxy`
- Add a solution file (`ClaudeCodeProxyDotNet.sln`) at the repository root
- Target .NET 10
- Confirm the project builds and runs with the default template

### Task 1.2 — Add NuGet Dependencies
Add the following packages to the project:
- `Microsoft.EntityFrameworkCore` — ORM for database access
- `Microsoft.EntityFrameworkCore.Sqlite` — SQLite provider
- `Microsoft.EntityFrameworkCore.Design` — EF Core tooling (migrations)
- No additional proxy library required; the proxy will be implemented with `HttpClient` middleware (see Phase 2 rationale)

### Task 1.3 — Project Structure & Conventions
Establish the folder layout:
```
ClaudeCodeProxy/
  Controllers/         # API controllers (stats endpoint in Phase 6)
  Data/                # DbContext, entity models, migrations
  Middleware/          # Proxy middleware and recording middleware
  Models/              # Request/response DTOs, DB entities
  Services/            # Business logic (token parsing, stats queries)
  wwwroot/             # Static files for the HTML UI (Phase 7)
  appsettings.json
  Program.cs
```

### Task 1.4 — Update CLAUDE.md and .gitignore
- Update `CLAUDE.md` with build, run, and test commands
- Ensure `.gitignore` excludes `*.db`, `*.db-shm`, `*.db-wal`, `bin/`, `obj/`

---

## Phase 2: Core Proxy Middleware

### Rationale
The proxy is implemented as a custom ASP.NET Core terminal middleware rather than using YARP. This gives direct control over request/response body capture and SSE stream forwarding — both required for Phase 3 and 4. YARP's transform pipeline makes full-body capture during streaming more complex without clear benefit at this scale.

### Task 2.1 — Configuration Model
- Add an `UpstreamOptions` class to hold the configurable upstream base URL
- Bind it from `appsettings.json` under a `"Upstream"` section
- Support override via the `ANTHROPIC_BASE_URL` environment variable (consistent with how Claude Code uses it)
- Validate that the upstream URL is present on startup; fail fast if missing

**Example `appsettings.json` section:**
```json
"Upstream": {
  "BaseUrl": "https://api.anthropic.com"
}
```

### Task 2.2 — Register a Named HttpClient
- Register a named `HttpClient` (`"upstream"`) in `Program.cs`
- Configure it with:
  - The upstream base URL from `UpstreamOptions`
  - A reasonable timeout (configurable, default 5 minutes to handle long LLM responses)
  - No automatic redirect following (pass through as-is)

### Task 2.3 — Implement the Proxy Middleware
Create `Middleware/ProxyMiddleware.cs` that acts as the terminal middleware for all incoming requests:

1. **Read and buffer the incoming request body** — read `HttpContext.Request.Body` into a `MemoryStream` so the body can both be forwarded and recorded
2. **Build the upstream request**:
   - Copy method, path, query string, and relevant headers (strip hop-by-hop headers: `Connection`, `Transfer-Encoding`, `Keep-Alive`, `Upgrade`, `Proxy-*`)
   - Attach the buffered request body as content
3. **Forward to upstream** using `HttpClient.SendAsync` with `HttpCompletionOption.ResponseHeadersRead` to enable streaming
4. **Copy response headers** back to `HttpContext.Response` (status code + headers, again stripping hop-by-hop)
5. **Stream the response body** back to the client:
   - For SSE (streaming) responses (`Content-Type: text/event-stream`): read in chunks and write to the client response stream immediately, accumulating a copy for recording
   - For non-streaming responses: read fully into a buffer, then write to client
6. **Pass captured data** to the recording service (Phase 3)

### Task 2.4 — Register Middleware in Program.cs
- Register `ProxyMiddleware` as the terminal handler (after routing, before the catch-all)
- Ensure it covers all paths that should be proxied (i.e., everything under `/`)

### Task 2.5 — Smoke Test the Proxy
- Manually test that a simple HTTP request forwarded through the proxy reaches the upstream and the response is returned correctly
- Verify headers are passed through appropriately (including `x-api-key` / `Authorization`)

### Task 2.6 — Unit Tests for ProxyMiddleware
Create an NUnit test project `ClaudeCodeProxy.Tests` and write unit tests for `ProxyMiddleware` using `RichardSzalay.MockHttp` to mock the upstream `HttpClient`:
- **Basic GET forwarding** — upstream status code and response body are returned to the client
- **POST with body** — request body and `Content-Type` are forwarded to the upstream correctly
- **Custom header forwarding** — headers such as `x-api-key` and `anthropic-version` are passed through
- **Hop-by-hop header stripping (request)** — `Connection`, `Host`, etc. are not forwarded to the upstream
- **Response header forwarding** — custom headers from the upstream response appear on the client response
- **`Content-Length` stripping (response)** — `Content-Length` is excluded so Kestrel sets it from actual write size
- **SSE streaming** — a `text/event-stream` response is forwarded in chunks and the full body reaches the client
- **502 on upstream connection failure** — `HttpRequestException` from the upstream results in a 502 response
- **504 on upstream timeout** — `TaskCanceledException` from the upstream results in a 504 response

---

## Phase 3: SQLite Database & Request Recording

### Task 3.1 — Define the Database Schema

Create two entity models in `Models/`:

**`ProxyRequest`** — one row per proxied HTTP request:
| Column | Type | Notes |
|---|---|---|
| `Id` | `long` PK | Auto-increment |
| `Timestamp` | `DateTime` | UTC time request was received |
| `Method` | `string` | HTTP method (GET, POST, …) |
| `Path` | `string` | Request path + query string |
| `RequestHeaders` | `string` | JSON-serialised relevant headers |
| `RequestBody` | `string?` | Raw request body (null if empty) |
| `ResponseStatusCode` | `int` | HTTP status returned to client |
| `ResponseHeaders` | `string` | JSON-serialised response headers |
| `ResponseBody` | `string?` | Raw response body (null if empty/binary) |
| `DurationMs` | `long` | Total proxy duration in milliseconds |

**`LlmUsage`** — one row per Anthropic LLM call (linked to `ProxyRequest`):
| Column | Type | Notes |
|---|---|---|
| `Id` | `long` PK | Auto-increment |
| `ProxyRequestId` | `long` FK | Foreign key to `ProxyRequests` |
| `Timestamp` | `DateTime` | UTC (same as parent request) |
| `Model` | `string?` | Model name from response (e.g., `claude-opus-4-6`) |
| `InputTokens` | `int` | From `usage.input_tokens` |
| `OutputTokens` | `int` | From `usage.output_tokens` |
| `CacheReadTokens` | `int` | From `usage.cache_read_input_tokens` (0 if absent) |
| `CacheCreationTokens` | `int` | From `usage.cache_creation_input_tokens` (0 if absent) |

### Task 3.2 — Create the DbContext
- Create `Data/ProxyDbContext.cs` with `DbSet<ProxyRequest>` and `DbSet<LlmUsage>`
- Configure the SQLite connection string in `appsettings.json` (default: `proxy.db` in the working directory)
- Register `ProxyDbContext` with the DI container using `AddDbContext`

### Task 3.3 — Create and Apply Initial Migration
- Run `dotnet ef migrations add InitialCreate` to generate the migration
- Run `dotnet ef database update` to apply it
- Confirm the SQLite file is created with the correct tables

### Task 3.4 — Implement the Recording Service
Create `Services/RecordingService.cs`:
- Accept a `ProxyRequest` entity (already populated by middleware) and save it to the database
- Use a fire-and-forget pattern or a background queue to avoid blocking the proxy response path
- Ensure write failures are logged but do not propagate to the client

### Task 3.5 — Wire Recording Into the Proxy Middleware
- After the response is fully sent to the client, call `RecordingService` with the captured request/response data
- Measure and record `DurationMs` using a `Stopwatch` started at the beginning of the middleware

---

## Phase 4: LLM Token Counting

### Task 4.1 — Detect Anthropic LLM Calls
- Identify requests that are Anthropic Messages API calls by checking:
  - Path ends with `/v1/messages` (or `/messages` as a suffix to accommodate path prefixes)
  - Method is `POST`
- Create a small helper `IsAnthropicMessagesCall(string path, string method)` used by both the middleware and token parser

### Task 4.2 — Implement Non-Streaming Token Parser
Create `Services/TokenUsageParser.cs`:
- Accept the full response body string
- Deserialise the JSON and extract:
  - `usage.input_tokens`
  - `usage.output_tokens`
  - `usage.cache_read_input_tokens` (optional)
  - `usage.cache_creation_input_tokens` (optional)
  - `model`
- Return a nullable result object; return `null` if parsing fails (malformed JSON, unexpected structure)

### Task 4.3 — Implement Streaming (SSE) Token Parser
For streaming responses (`Content-Type: text/event-stream`):
- The accumulated SSE body collected in Task 2.3 contains newline-delimited `data:` lines
- Parse each `data:` line as JSON
- Find the event where `type == "message_delta"` (which contains `usage.output_tokens`) and the event where `type == "message_start"` (which contains `usage.input_tokens` in its nested `message.usage`)
- Alternatively, look for the final `"usage"` block in the `message_delta` event which Anthropic includes with cumulative token counts
- Return the same result object as the non-streaming parser

### Task 4.4 — Save Token Usage After Recording
- In `RecordingService`, after saving the `ProxyRequest`, call the appropriate token parser
- If parsing succeeds, create and save an `LlmUsage` record linked to the saved `ProxyRequest`
- Log a warning if token parsing fails for a path/method that was identified as an LLM call

### Task 4.5 — Unit Tests for Token Parsers
- Create an NUnit test project `ClaudeCodeProxy.Tests`
- Write unit tests for `TokenUsageParser` using fixture JSON samples:
  - A non-streaming `messages` response with usage
  - A streaming SSE response with multiple events including a `message_delta` usage event
  - Malformed/empty responses (should return null gracefully)

---

## Phase 5: Configuration, Logging & Hardening

### Task 5.1 — Structured Logging
- Configure Serilog or the built-in `ILogger` to log:
  - Each proxied request: method, path, upstream status, duration
  - Recording failures (non-fatal)
  - Token parsing warnings
- Use structured logging fields so logs can be filtered by path/status/duration

### Task 5.2 — Error Handling & Edge Cases
- Handle upstream connection failures: return `502 Bad Gateway` to the client with a clear message
- Handle upstream timeouts: return `504 Gateway Timeout`
- Handle very large request/response bodies: cap the stored body size at a configurable limit (e.g. 1 MB) and store a truncation note rather than the full body

---

## Phase 6 (Stretch): Analytics API Endpoint

### Task 6.1 — Stats Query Service
Create `Services/StatsService.cs` with two query methods:
- `GetRequestsPerHour(DateTime from, DateTime to)` — returns a list of `{ Hour, RequestCount, LlmRequestCount, TotalInputTokens, TotalOutputTokens }`
- `GetRequestsPerDay(DateTime from, DateTime to)` — same shape but bucketed by day
Both methods query the `ProxyRequests` and `LlmUsage` tables via EF Core

### Task 6.2 — Stats Controller
Create `Controllers/StatsController.cs` with:
- `GET /api/stats/hourly?from=...&to=...` — returns hourly aggregates as JSON
- `GET /api/stats/daily?from=...&to=...` — returns daily aggregates as JSON
- Default `from` to 7 days ago and `to` to now if query params are omitted
- Return `application/json`; include CORS headers if needed for the HTML UI

### Task 6.3 — Unit Tests for Stats Queries
- Add NUnit tests to `ClaudeCodeProxy.Tests` for `StatsService`
- Use an in-memory SQLite database (EF Core supports this) seeded with known data
- Assert correct bucketing and aggregation

---

## Phase 7 (Stretch): Angular SPA Dashboard

### Rationale
Rather than a static HTML page, Phase 7 delivers a proper Angular Single Page Application (SPA) that consumes the stats API from Phase 6. The SPA is developed as a standalone Angular project, built to static assets, and served by the .NET backend via `UseStaticFiles`. During development the Angular dev server proxies API calls to the .NET backend.

### Task 7.1 — Scaffold the Angular Project
- Create the Angular app in `src/ClaudeCodeProxyAngular/` using the Angular CLI:
  ```bash
  ng new ClaudeCodeProxyAngular --routing=false --style=scss --standalone
  ```
- Add it to `.gitignore` exclusions for `node_modules/` and `dist/`
- Record the Angular CLI version and Node requirement in `CLAUDE.md`

### Task 7.2 — Configure the Angular Dev Proxy
- Create `src/ClaudeCodeProxyAngular/proxy.conf.json` to forward `/api/**` to the .NET backend during development:
  ```json
  {
    "/api": {
      "target": "http://localhost:5000",
      "secure": false,
      "changeOrigin": true
    }
  }
  ```
- Add a `start` script to `package.json` that passes `--proxy-config proxy.conf.json`
- Ensure the .NET backend has CORS enabled for the Angular dev-server origin (`http://localhost:4200`) so that browser requests are not blocked during development

### Task 7.3 — Create the Stats API Service
Create `src/app/stats.service.ts`:
- Define TypeScript interfaces matching the API response shapes:
  - `HourlyStat { hour: string; requestCount: number; llmRequestCount: number; totalInputTokens: number; totalOutputTokens: number; }`
  - `DailyStat` — same shape with a `day` field instead of `hour`
- Inject `HttpClient` and expose two methods:
  - `getHourly(from?: string, to?: string): Observable<HourlyStat[]>`
  - `getDaily(from?: string, to?: string): Observable<DailyStat[]>`

### Task 7.4 — Build the Dashboard Components
Create two standalone Angular components:

**`HourlyStatsComponent`** (`src/app/hourly-stats/`):
- Fetches and displays hourly data for the last 24 hours in a sortable table
- Columns: Hour, Requests, LLM Requests, Input Tokens, Output Tokens
- Shows a loading spinner while fetching and an error banner on failure

**`DailyStatsComponent`** (`src/app/daily-stats/`):
- Fetches and displays daily data for the last 30 days in a sortable table
- Same columns as above, with the time bucket labelled "Day"
- Includes a simple bar chart rendered with Angular's `NgStyle` and CSS flexbox (no external charting library) showing daily total requests over time

**`AppComponent`** — update the root component to:
- Display both components in a single-page layout with a header
- Show the proxy name and current date/time in the header

### Task 7.5 — Build Angular Output into wwwroot
- Add an `npm run build` script that outputs to `src/ClaudeCodeProxy/wwwroot/` (using Angular's `outputPath` in `angular.json`)
- Add `app.UseStaticFiles()` and a fallback route to `Program.cs` so the .NET server serves `index.html` for any unmatched path (required for Angular's client-side router if routing is later added):
  ```csharp
  app.MapFallbackToFile("index.html");
  ```
- Confirm navigating to `http://localhost:<port>/` serves the Angular app after a build

### Task 7.6 — Update CLAUDE.md
Document the full development workflow:
- How to run the Angular dev server alongside the .NET backend
- How to build the SPA and embed it into the .NET static files
- Node/npm version requirements

---

## Suggested Build Order

```
Phase 1  →  Phase 2  →  Phase 3  →  Phase 4  →  Phase 5
                                                    ↓
                                               Phase 6  →  Phase 7
```

Phase 6 must be completed before Phase 7, as the Angular SPA depends on the stats API endpoints.

---

## Definition of Done for Version 1

- [ ] The proxy starts and successfully forwards all HTTP traffic to the configured upstream URL
- [ ] A Claude Code session using `ANTHROPIC_BASE_URL=http://localhost:<port>` works end-to-end without errors
- [ ] Every proxied request is written to the SQLite database
- [ ] Input and output token counts are stored for all `POST /v1/messages` calls, including streaming calls
- [ ] The database file persists across proxy restarts
- [ ] The project builds cleanly with `dotnet build`
- [ ] Unit tests pass with `dotnet test`
- [ ] `CLAUDE.md` documents how to build, run, and test the proxy
- [ ] An Angular SPA served at `http://localhost:<port>/` displays hourly and daily token usage stats fetched from the API
