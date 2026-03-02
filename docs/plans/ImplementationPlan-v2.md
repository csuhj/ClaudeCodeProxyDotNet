# Implementation Plan — Version 2

## Overview

This plan extends the Version 1 application with the two requirements from the "Further Requirements For Version 2" section:

> **Requirement 7.** Extend the SPA UI (and API that backs it) to be able to list all the LLM requests made in the last day.
>
> **Requirement 8.** In the UI, allow each request to be drilled into to show the full request body and results in a manner that is readable for a human user.

The v1 application already captures and stores everything needed: full request bodies, full response bodies, headers, timestamps, HTTP metadata, and extracted token counts. Version 2 adds the API endpoints and Angular components to surface this data.

---

## Architecture Summary

The implementation follows the same layered pattern established in v1:

```
Angular UI  ←→  New API Endpoints  ←→  New Service  ←→  Extended Repository  ←→  SQLite
```

New artefacts per layer:

| Layer | New Artefact |
|---|---|
| Models/DTOs | `LlmRequestSummary`, `LlmRequestDetail` |
| Repository | `GetLlmRequestsAsync`, `GetLlmRequestByIdAsync` on `IRecordingRepository` |
| Service | `IRequestsService` + `RequestsService` |
| Controller | `RequestsController` (`GET /api/requests`, `GET /api/requests/{id}`) |
| Angular Service | New methods on a `RequestsService` Angular service |
| Angular Components | `LlmRequestsListComponent`, `RequestDetailPanelComponent` |

---

## Phase 1: Backend — Data Transfer Objects

### Task 1.1 — Create `LlmRequestSummary` DTO

Create `src/ClaudeCodeProxy/Models/LlmRequestSummary.cs`.

This DTO is used for the list view — it contains only the columns needed to render a row in the requests table, without the potentially large request/response body strings.

Fields:
| Field | Type | Source |
|---|---|---|
| `Id` | `long` | `ProxyRequest.Id` |
| `Timestamp` | `DateTime` | `ProxyRequest.Timestamp` |
| `Method` | `string` | `ProxyRequest.Method` |
| `Path` | `string` | `ProxyRequest.Path` |
| `ResponseStatusCode` | `int` | `ProxyRequest.ResponseStatusCode` |
| `DurationMs` | `long` | `ProxyRequest.DurationMs` |
| `Model` | `string?` | `LlmUsage.Model` (null if no usage record) |
| `InputTokens` | `int?` | `LlmUsage.InputTokens` (null if no usage record) |
| `OutputTokens` | `int?` | `LlmUsage.OutputTokens` (null if no usage record) |
| `CacheReadTokens` | `int?` | `LlmUsage.CacheReadTokens` (null if no usage record) |
| `CacheCreationTokens` | `int?` | `LlmUsage.CacheCreationTokens` (null if no usage record) |

### Task 1.2 — Create `LlmRequestDetail` DTO

Create `src/ClaudeCodeProxy/Models/LlmRequestDetail.cs`.

This DTO is used for the drill-down view. It includes all fields from `LlmRequestSummary` plus the full body and header content.

Fields — extend `LlmRequestSummary` with:
| Field | Type | Source |
|---|---|---|
| `RequestHeaders` | `string` | `ProxyRequest.RequestHeaders` (raw JSON string) |
| `RequestBody` | `string?` | `ProxyRequest.RequestBody` |
| `ResponseHeaders` | `string` | `ProxyRequest.ResponseHeaders` (raw JSON string) |
| `ResponseBody` | `string?` | `ProxyRequest.ResponseBody` |
| `IsStreaming` | `bool` | Derived: `true` if `ResponseHeaders` contains `text/event-stream` |

The `IsStreaming` flag is included so the Angular frontend knows which rendering path to take for the response body (see Phase 5).

---

## Phase 2: Backend — Repository Extension

The existing `IRecordingRepository` in `src/ClaudeCodeProxy/Data/IRecordingRepository.cs` currently exposes:
- `AddAsync` — save a request
- `GetStatsProjectionsAsync` — return lightweight aggregation projections

Two new query methods are needed.

### Task 2.1 — Add `GetLlmRequestsAsync` to `IRecordingRepository`

Add the following method signature to the interface:

```csharp
Task<List<LlmRequestSummary>> GetLlmRequestsAsync(
    DateTime from,
    DateTime to,
    int skip,
    int take,
    CancellationToken ct = default);
```

- Filters `ProxyRequests` to the `[from, to)` time window
- Filters to rows that have an associated `LlmUsage` record (i.e. confirmed LLM calls — inner join with `LlmUsages`)
- Orders by `Timestamp` descending (most recent first)
- Applies `Skip`/`Take` for pagination
- Projects directly to `LlmRequestSummary` in the EF Core query to avoid loading body columns unnecessarily

### Task 2.2 — Add `GetLlmRequestByIdAsync` to `IRecordingRepository`

Add the following method signature to the interface:

```csharp
Task<LlmRequestDetail?> GetLlmRequestByIdAsync(long id, CancellationToken ct = default);
```

- Loads the `ProxyRequest` row with the given `Id`, including its related `LlmUsage` (left join)
- Returns `null` if not found
- Projects to `LlmRequestDetail`
- Derives `IsStreaming` by checking whether the stored `ResponseHeaders` JSON contains `text/event-stream` as a `Content-Type` value

### Task 2.3 — Implement in `RecordingRepository`

Add the concrete implementations of both methods to `src/ClaudeCodeProxy/Data/RecordingRepository.cs`:

- `GetLlmRequestsAsync`: Use EF Core LINQ with `.Join` (or `.Include` + `.Where`) on `LlmUsages`, order by `Timestamp` descending, apply pagination, project via `Select` to `LlmRequestSummary`
- `GetLlmRequestByIdAsync`: Use `.SingleOrDefaultAsync` with a left join to `LlmUsages`, project via `Select` to `LlmRequestDetail`

For `IsStreaming` derivation, deserialise the `ResponseHeaders` JSON string and check for a key matching `Content-Type` with a value containing `text/event-stream`. Use `System.Text.Json` for this (same library already used elsewhere in the project).

---

## Phase 3: Backend — Service

### Task 3.1 — Create `IRequestsService`

Create `src/ClaudeCodeProxy/Services/IRequestsService.cs`:

```csharp
public interface IRequestsService
{
    Task<List<LlmRequestSummary>> GetRecentLlmRequestsAsync(
        DateTime from,
        DateTime to,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<LlmRequestDetail?> GetLlmRequestDetailAsync(long id, CancellationToken ct = default);
}
```

### Task 3.2 — Create `RequestsService`

Create `src/ClaudeCodeProxy/Services/RequestsService.cs`:

- Implement `IRequestsService`
- `GetRecentLlmRequestsAsync`:
  - Validates that `pageSize` is between 1 and 200 (clamp or throw); default is 50
  - Calculates `skip = page * pageSize`
  - Delegates to `IRecordingRepository.GetLlmRequestsAsync`
- `GetLlmRequestDetailAsync`:
  - Delegates directly to `IRecordingRepository.GetLlmRequestByIdAsync`
  - Returns `null` if the repository returns `null`

Register `RequestsService` as a scoped service in `Program.cs` (same pattern as `StatsService`).

---

## Phase 4: Backend — Controller

### Task 4.1 — Create `RequestsController`

Create `src/ClaudeCodeProxy/Controllers/RequestsController.cs`:

**Route:** `[ApiController] [Route("api/requests")]`

---

**Endpoint 1 — List LLM requests**

```
GET /api/requests
```

Query parameters:
| Parameter | Type | Default | Description |
|---|---|---|---|
| `from` | `DateTime?` | 24 hours ago (UTC) | Start of time window (inclusive) |
| `to` | `DateTime?` | now (UTC) | End of time window (exclusive) |
| `page` | `int` | `0` | Zero-based page index |
| `pageSize` | `int` | `50` | Results per page (max 200) |

Response: `200 OK` with `application/json` body of type `List<LlmRequestSummary>`.

Default `from` and `to` behaviour: when omitted, default to the last 24 hours (i.e. `to = DateTime.UtcNow`, `from = to.AddDays(-1)`). This satisfies Requirement 7 ("list all the LLM requests made in the last day") with no query parameters required.

---

**Endpoint 2 — Get single request detail**

```
GET /api/requests/{id}
```

Path parameter: `id` (long) — the `ProxyRequest.Id`.

Response:
- `200 OK` with `application/json` body of type `LlmRequestDetail` — if found
- `404 Not Found` — if no record with that `Id` exists

---

### Task 4.2 — Register Controller in `Program.cs`

The existing `Program.cs` already calls `builder.Services.AddControllers()` and `app.MapControllers()`. No changes should be needed to the pipeline — the new controller is discovered automatically.

Register `RequestsService` as a scoped dependency:
```csharp
builder.Services.AddScoped<IRequestsService, RequestsService>();
```

---

## Phase 5: Backend — Tests

### Task 5.1 — Repository Tests for New Query Methods

Add tests to `test/ClaudeCodeProxy.Tests/` for `RecordingRepository`:

- **List returns only LLM requests**: Seed two `ProxyRequest` rows, one with an `LlmUsage` record and one without. Call `GetLlmRequestsAsync`. Assert only the one with `LlmUsage` is returned.
- **List respects time window**: Seed requests at various timestamps. Assert only those within `[from, to)` are returned.
- **List is ordered newest-first**: Seed multiple LLM requests. Assert they are returned in descending timestamp order.
- **Pagination works**: Seed 5 LLM requests, call with `take=2, skip=2`. Assert the correct two records are returned.
- **Detail returns null for unknown id**: Call `GetLlmRequestByIdAsync(99999)`. Assert result is `null`.
- **Detail returns full fields**: Seed a `ProxyRequest` with a known body and headers. Call `GetLlmRequestByIdAsync(id)`. Assert all fields including `RequestBody`, `ResponseBody`, `RequestHeaders`, and `ResponseHeaders` are present.
- **IsStreaming is true for SSE responses**: Seed a `ProxyRequest` whose `ResponseHeaders` JSON contains `Content-Type: text/event-stream`. Assert `LlmRequestDetail.IsStreaming == true`.
- **IsStreaming is false for JSON responses**: Seed a `ProxyRequest` whose `ResponseHeaders` JSON contains `Content-Type: application/json`. Assert `LlmRequestDetail.IsStreaming == false`.

Use an in-memory SQLite database as established by the v1 test patterns.

### Task 5.2 — Service Tests for `RequestsService`

Add tests to verify `RequestsService` correctly delegates and applies defaults:

- **Default time window covers last 24 hours**: Verify (via mocked repository) that when `from`/`to` are null, the service passes a range of approximately the last 24 hours.
- **PageSize is clamped to 200**: Call with `pageSize=500`. Assert the repository receives `take=200`.

### Task 5.3 — Controller Tests for `RequestsController`

Add integration-style tests using `WebApplicationFactory` or `TestServer`:

- **GET /api/requests returns 200 with empty list when no data**: Assert HTTP 200 and empty JSON array.
- **GET /api/requests/{id} returns 404 for unknown id**: Assert HTTP 404.
- **GET /api/requests/{id} returns 200 with detail for known id**: Seed data, assert HTTP 200 and correct DTO fields.

---

## Phase 6: Frontend — Angular Service

### Task 6.1 — Define TypeScript Interfaces

Create or extend `src/ClaudeCodeProxyAngular/src/app/services/requests.service.ts`.

Define two TypeScript interfaces matching the backend DTOs:

```typescript
export interface LlmRequestSummary {
  id: number;
  timestamp: string;        // ISO 8601 UTC
  method: string;
  path: string;
  responseStatusCode: number;
  durationMs: number;
  model: string | null;
  inputTokens: number | null;
  outputTokens: number | null;
  cacheReadTokens: number | null;
  cacheCreationTokens: number | null;
}

export interface LlmRequestDetail extends LlmRequestSummary {
  requestHeaders: string;   // JSON string (deserialise on the frontend as needed)
  requestBody: string | null;
  responseHeaders: string;  // JSON string
  responseBody: string | null;
  isStreaming: boolean;
}
```

### Task 6.2 — Create `RequestsService` Angular Service

In the same file, create an injectable `RequestsService`:

```typescript
@Injectable({ providedIn: 'root' })
export class RequestsService {
  getRecent(from?: string, to?: string, page = 0, pageSize = 50): Observable<LlmRequestSummary[]>
  getDetail(id: number): Observable<LlmRequestDetail>
}
```

- `getRecent`: calls `GET /api/requests` with optional query parameters. When `from`/`to` are omitted, the backend defaults to the last 24 hours, so the component need not pass them for the default view.
- `getDetail`: calls `GET /api/requests/{id}`.
- Both methods use `HttpClient` (already provided via `provideHttpClient()` in `app.config.ts`).

---

## Phase 7: Frontend — Request List Component

### Task 7.1 — Create `LlmRequestsListComponent`

Create `src/ClaudeCodeProxyAngular/src/app/components/llm-requests-list/` with the following files:
- `llm-requests-list.ts`
- `llm-requests-list.html`
- `llm-requests-list.scss`
- `llm-requests-list.spec.ts`

**Component behaviour:**

- On `ngOnInit`, call `RequestsService.getRecent()` with no arguments (last 24 hours, page 0, pageSize 50)
- Use Angular signals for reactive state: `data = signal<LlmRequestSummary[]>([])`, `loading = signal(true)`, `error = signal<string | null>(null)`
- Show loading indicator while fetching, error message on failure, "No LLM requests in the last 24 hours" when the list is empty
- On success, populate the data signal and render the table

**Table columns:**

| Column | Value | Notes |
|---|---|---|
| Time (UTC) | `timestamp` formatted as `dd MMM HH:mm:ss` | UTC timezone |
| Model | `model` | e.g. `claude-sonnet-4-6`; show `—` if null |
| Path | `path` | Truncate to ~60 chars with ellipsis if longer |
| Status | `responseStatusCode` | Colour-code: green for 2xx, red for 4xx/5xx |
| Duration | `durationMs` formatted as `Xms` or `X.Xs` | Human-friendly |
| Input Tokens | `inputTokens` | Right-aligned; show `—` if null |
| Output Tokens | `outputTokens` | Right-aligned; show `—` if null |

**Row interaction:**

- Each row is clickable (cursor: pointer)
- Clicking a row emits an `@Output() requestSelected = new EventEmitter<number>()` event passing the request `id`
- This event is handled by the parent `AppComponent` to show the detail panel (Phase 8)

### Task 7.2 — Add to `AppComponent`

Update `src/ClaudeCodeProxyAngular/src/app/app.ts` and `app.html`:

- Import `LlmRequestsListComponent` and `RequestDetailPanelComponent`
- Add a `selectedRequestId = signal<number | null>(null)` state variable
- Add `<app-llm-requests-list (requestSelected)="selectedRequestId.set($event)" />` to the template, positioned between the existing hourly and daily stat tables (or below them — see layout note)
- When `selectedRequestId` is non-null, render the detail panel

**Suggested layout order in `app.html`:**
1. Header (existing)
2. LLM Requests List (new) — primary V2 feature, positioned prominently
3. Hourly Stats (existing)
4. Daily Stats (existing)
5. Footer (existing)

---

## Phase 8: Frontend — Request Detail Panel Component

### Task 8.1 — Create `RequestDetailPanelComponent`

Create `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/` with:
- `request-detail-panel.ts`
- `request-detail-panel.html`
- `request-detail-panel.scss`
- `request-detail-panel.spec.ts`

**Component inputs/outputs:**

```typescript
@Input({ required: true }) requestId!: number;
@Output() closed = new EventEmitter<void>();
```

When `requestId` changes, the component fetches `RequestsService.getDetail(requestId)`.

**Layout:** render as a panel that appears below the request list or as a full-width section beneath it (not a modal/overlay, keeping it accessible without a router). Include a "Close" button that emits `closed`, which the parent uses to reset `selectedRequestId` to `null`.

**Sections to render:**

---

**Section 1 — Summary metadata**

Display a compact header row with:
- Timestamp (formatted, UTC)
- HTTP method + path
- Response status code (colour-coded as in the list)
- Duration
- Model name (if available)
- Token counts: Input / Output / Cache read / Cache creation

---

**Section 2 — Request body**

Label: "Request"

The stored `RequestBody` for Anthropic Messages API calls is a JSON object. Render it in two ways, togglable via a "Raw" / "Formatted" toggle button:

- **Formatted view** (default): Parse the JSON and render the conversation in a human-readable layout:
  - Show `model`, `max_tokens`, `temperature` (if present) as labelled fields
  - If a `system` prompt is present, show it under a "System" heading in a styled block
  - For each entry in `messages`, render a conversation bubble-style block with:
    - Role label (`user` or `assistant`) styled distinctly
    - Content text (handle both `string` content and `ContentBlock[]` content — extract `text` blocks)
  - If `tools` are defined in the request, list their names and descriptions in a collapsible "Tools" section

- **Raw view**: Display the JSON string in a `<pre>` block with indentation (use `JSON.stringify(parsed, null, 2)`)

If `RequestBody` is null or not valid JSON, show the raw text (or "No request body").

---

**Section 3 — Response body**

Label: "Response"

The stored `ResponseBody` is either:
- A JSON string (non-streaming call, `isStreaming === false`)
- A sequence of Server-Sent Events lines (streaming call, `isStreaming === true`)

Provide a "Raw" / "Formatted" toggle.

**For non-streaming (`isStreaming === false`):**
- **Formatted view**: Parse the JSON. Show:
  - `stop_reason` and `stop_sequence` as labelled fields
  - Token usage summary (input / output)
  - For each block in `content`, render the text content in a styled block
- **Raw view**: Pretty-print the JSON in a `<pre>` block

**For streaming (`isStreaming === true`):**
- **Formatted view** (default): Parse the SSE lines:
  - Split on newlines, find lines beginning with `data: `
  - Parse each as JSON, ignoring `[DONE]`
  - Reconstruct the assistant's text by concatenating all `text_delta` values from `content_block_delta` events where `delta.type === "text_delta"`
  - Find the final `usage` block from the `message_delta` event (type `"message_delta"`) for token counts
  - Find the model name from the `message_start` event
  - Display: model, token summary, then the reconstructed assistant response text in a styled block
- **Raw view**: Display the raw SSE event lines in a `<pre>` block

If `ResponseBody` is null, show "No response body captured".

---

**Section 4 — Headers (collapsible)**

Provide two collapsible `<details>` / `<summary>` sections (native HTML, no library):
- "Request Headers" — parse `RequestHeaders` JSON and render as a definition list of key → value pairs
- "Response Headers" — same for `ResponseHeaders`

These are collapsed by default to keep the panel compact.

---

### Task 8.2 — Styling

In `request-detail-panel.scss`:
- Give each section a distinct background shade to separate them visually
- Style the conversation message blocks (user vs assistant) with different background colours, similar to a chat UI
- Style the `<pre>` raw-view blocks with monospace font, horizontal scroll, and a dark background
- Make the panel scrollable if content overflows
- Add a sticky "Close" button at the top-right of the panel

---

## Phase 9: Integration and Definition of Done

### Task 9.1 — Wire `AppComponent` State

In `app.ts`:
- `selectedRequestId = signal<number | null>(null)` (new signal)
- Handler: `onRequestSelected(id: number) { this.selectedRequestId.set(id); }`
- Handler: `onDetailClosed() { this.selectedRequestId.set(null); }`

In `app.html`:
```html
<app-llm-requests-list (requestSelected)="onRequestSelected($event)" />

@if (selectedRequestId() !== null) {
  <app-request-detail-panel
    [requestId]="selectedRequestId()!"
    (closed)="onDetailClosed()" />
}
```

### Task 9.2 — End-to-End Smoke Test

Manually verify the complete flow using a live proxy session:

1. Start the .NET backend (`dotnet run --project src/ClaudeCodeProxy`)
2. Run at least one Claude Code session through the proxy to generate LLM request records
3. Open the dashboard at `http://localhost:5051` (dev) or `http://localhost:5000` (production build)
4. Confirm the LLM Requests list shows requests from the last 24 hours
5. Click a row and confirm the detail panel opens
6. Confirm the formatted request body shows the conversation messages legibly
7. For a streaming request, confirm the formatted response shows the reconstructed assistant text
8. Confirm the "Raw" toggle shows the underlying JSON / SSE data
9. Confirm the "Close" button returns to the list view

---

## Suggested Build Order

```
Phase 1 (DTOs)
    ↓
Phase 2 (Repository extension)  ←→  Phase 6 (Angular interfaces)
    ↓
Phase 3 (Service)               ←→  Phase 7 (List component)
    ↓
Phase 4 (Controller)            ←→  Phase 8 (Detail component)
    ↓                                         ↓
Phase 5 (Backend tests)         ←→  Phase 9 (Integration + smoke test)
```

Backend phases (1–4) and frontend phases (6–8) can be developed in parallel once the DTO shapes are agreed (Phase 1 is the dependency for both tracks).

---

## Definition of Done for Version 2

- [ ] `GET /api/requests` returns a JSON list of LLM requests from the last 24 hours with no query parameters
- [ ] `GET /api/requests/{id}` returns the full request and response body for a known request id, and 404 for unknown ids
- [ ] The Angular dashboard renders an "LLM Requests" section listing the last 24 hours of LLM calls with timestamp, model, path, status, duration, and token counts
- [ ] Clicking a row in the list opens a detail panel
- [ ] The detail panel shows request metadata (timestamp, method, path, model, tokens, duration)
- [ ] The detail panel shows the request conversation (system prompt, messages) in a human-readable formatted view
- [ ] The detail panel shows the response text in a human-readable formatted view, correctly handling both streaming (SSE) and non-streaming (JSON) response bodies
- [ ] A "Raw" toggle on both request and response sections shows the underlying JSON or SSE text
- [ ] Request and response headers are accessible in collapsed sections
- [ ] The detail panel can be dismissed to return to the list view
- [ ] All new backend tests pass with `dotnet test`
- [ ] The project builds cleanly with `dotnet build` and `npm run build`
