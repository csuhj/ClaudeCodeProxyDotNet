# V2 Implementation TODO

## Phase 1: Backend — Data Transfer Objects

- [x] **Task 1.1** — Create `LlmRequestSummary` DTO (`src/ClaudeCodeProxy/Models/LlmRequestSummary.cs`) with fields: `Id`, `Timestamp`, `Method`, `Path`, `ResponseStatusCode`, `DurationMs`, `Model`, `InputTokens`, `OutputTokens`, `CacheReadTokens`, `CacheCreationTokens`
- [x] **Task 1.2** — Create `LlmRequestDetail` DTO (`src/ClaudeCodeProxy/Models/LlmRequestDetail.cs`) extending `LlmRequestSummary` with: `RequestHeaders`, `RequestBody`, `ResponseHeaders`, `ResponseBody`, `IsStreaming`

## Phase 2: Backend — Repository Extension

- [x] **Task 2.1** — Add `GetLlmRequestsAsync(DateTime from, DateTime to, int skip, int take, CancellationToken ct)` to `IRecordingRepository` — filters to LLM-only requests in time window, ordered newest-first, paginated, projected to `LlmRequestSummary`
- [x] **Task 2.2** — Add `GetLlmRequestByIdAsync(long id, CancellationToken ct)` to `IRecordingRepository` — returns `LlmRequestDetail?`, derives `IsStreaming` from stored `ResponseHeaders` JSON
- [x] **Task 2.3** — Implement both new methods in `RecordingRepository` using EF Core LINQ projections; derive `IsStreaming` via `System.Text.Json` check on `Content-Type` header value

## Phase 3: Backend — Service

- [x] **Task 3.1** — Create `IRequestsService` interface (`src/ClaudeCodeProxy/Services/IRequestsService.cs`) with `GetRecentLlmRequestsAsync` and `GetLlmRequestDetailAsync` methods
- [x] **Task 3.2** — Create `RequestsService` implementation (`src/ClaudeCodeProxy/Services/RequestsService.cs`): clamps `pageSize` to max 200, calculates `skip`, delegates to repository; register as scoped in `Program.cs`

## Phase 4: Backend — Controller

- [ ] **Task 4.1** — Create `RequestsController` (`src/ClaudeCodeProxy/Controllers/RequestsController.cs`) with two endpoints:
  - `GET /api/requests` — list LLM requests, defaults to last 24 hours, supports `from`/`to`/`page`/`pageSize` query params, returns `List<LlmRequestSummary>`
  - `GET /api/requests/{id}` — returns `LlmRequestDetail` or 404
- [ ] **Task 4.2** — Register `IRequestsService` / `RequestsService` as scoped in `Program.cs`

## Phase 5: Backend — Tests

- [ ] **Task 5.1** — Repository tests for `GetLlmRequestsAsync`:
  - [ ] Returns only requests that have an associated `LlmUsage` record
  - [ ] Respects the `[from, to)` time window
  - [ ] Returns results ordered newest-first
  - [ ] Pagination (`skip`/`take`) returns the correct page of results
- [ ] **Task 5.1 (cont.)** — Repository tests for `GetLlmRequestByIdAsync`:
  - [ ] Returns `null` for an unknown id
  - [ ] Returns all fields (including body and headers) for a known id
  - [ ] Sets `IsStreaming = true` when `ResponseHeaders` contains `text/event-stream`
  - [ ] Sets `IsStreaming = false` when `ResponseHeaders` contains `application/json`
- [ ] **Task 5.2** — Service tests for `RequestsService`:
  - [ ] `pageSize` is clamped to 200 when a larger value is passed
- [ ] **Task 5.3** — Controller tests for `RequestsController`:
  - [ ] `GET /api/requests` returns HTTP 200 with an empty array when no data exists
  - [ ] `GET /api/requests/{id}` returns HTTP 404 for an unknown id
  - [ ] `GET /api/requests/{id}` returns HTTP 200 with correct DTO fields for a known id

## Phase 6: Frontend — Angular Service

- [ ] **Task 6.1** — Define TypeScript interfaces in `src/app/services/requests.service.ts`: `LlmRequestSummary` and `LlmRequestDetail` (extending summary with body/header/isStreaming fields)
- [ ] **Task 6.2** — Create injectable `RequestsService` with `getRecent(from?, to?, page?, pageSize?)` and `getDetail(id)` methods using `HttpClient`

## Phase 7: Frontend — Request List Component

- [ ] **Task 7.1** — Create `LlmRequestsListComponent` (`src/app/components/llm-requests-list/`):
  - [ ] Fetches last 24 hours of LLM requests on `ngOnInit` using signals for `data`, `loading`, `error`
  - [ ] Renders table with columns: Time (UTC), Model, Path (truncated), Status (colour-coded), Duration, Input Tokens, Output Tokens
  - [ ] Shows loading indicator, error banner, and empty-state message appropriately
  - [ ] Emits `requestSelected` output event with the request `id` when a row is clicked
- [ ] **Task 7.2** — Update `AppComponent` (`app.ts` / `app.html`) to import and include the list component; add `selectedRequestId` signal; position the list prominently at the top of the page content (above hourly/daily stats)

## Phase 8: Frontend — Request Detail Panel Component

- [ ] **Task 8.1** — Create `RequestDetailPanelComponent` (`src/app/components/request-detail-panel/`):
  - [ ] Accepts `requestId` input; fetches detail on input change; emits `closed` output
  - [ ] **Section 1 — Metadata**: renders timestamp, method + path, status code (colour-coded), duration, model, token counts
  - [ ] **Section 2 — Request body**: formatted view (system prompt block, conversation message bubbles per role, collapsible tools list) and raw JSON view, togglable; handles null/invalid body gracefully
  - [ ] **Section 3 — Response body (non-streaming)**: formatted view (stop reason, token summary, content blocks) and raw JSON view, togglable
  - [ ] **Section 3 — Response body (streaming)**: formatted view (reconstructed assistant text from `text_delta` SSE events, token summary from `message_delta`) and raw SSE view, togglable
  - [ ] **Section 4 — Headers**: two native `<details>`/`<summary>` collapsible sections for request and response headers, collapsed by default
- [ ] **Task 8.2** — Style `request-detail-panel.scss`: distinct background per section, chat-bubble styling for user/assistant messages, monospace dark `<pre>` blocks for raw views, scrollable panel, sticky close button

## Phase 9: Integration

- [ ] **Task 9.1** — Wire `AppComponent` state: add `onRequestSelected(id)` and `onDetailClosed()` handlers; conditionally render `RequestDetailPanelComponent` when `selectedRequestId` is non-null
- [ ] **Task 9.2** — End-to-end smoke test: run proxy, generate LLM traffic, confirm list appears, confirm detail panel opens on row click, confirm formatted and raw views work for both streaming and non-streaming requests, confirm close button works
