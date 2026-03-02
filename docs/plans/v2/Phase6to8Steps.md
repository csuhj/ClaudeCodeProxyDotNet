# V2 Implementation Steps — Phases 6 to 8

This document records the exact steps followed to implement the Angular frontend phases of the V2 plan.

---

## Phase 6: Frontend — Angular Service

### Files created

- `src/ClaudeCodeProxyAngular/src/app/services/requests.service.ts`
- `src/ClaudeCodeProxyAngular/src/app/services/requests.service.spec.ts`

### Step-by-step

**Step 1 — Reviewed the existing `StatsService`** (`src/app/services/stats.service.ts`) to understand the established patterns:
- `@Injectable({ providedIn: 'root' })` with `inject(HttpClient)`
- `HttpParams` for optional query string building
- Returns `Observable<T>` directly from `HttpClient.get`

**Step 2 — Defined the two TypeScript interfaces** in `requests.service.ts` to mirror the backend DTOs:

```typescript
export interface LlmRequestSummary {
  id: number;
  timestamp: string;        // ISO 8601 UTC string
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
  requestHeaders: string;   // raw JSON string
  requestBody: string | null;
  responseHeaders: string;  // raw JSON string
  responseBody: string | null;
  isStreaming: boolean;
}
```

**Step 3 — Implemented `RequestsService`:**
- `getRecent(from?, to?, page = 0, pageSize = 50)` — builds `HttpParams` conditionally (only appends `from`/`to` when provided, always appends `page`/`pageSize`) and calls `GET /api/requests`
- `getDetail(id: number)` — calls `GET /api/requests/{id}`

**Step 4 — Wrote `requests.service.spec.ts`** following the same pattern as `stats.service.spec.ts`:
- Uses `provideHttpClient()` + `provideHttpClientTesting()` + `HttpTestingController`
- Verifies `getRecent()` sends correct query params (omits `from`/`to` when absent, always includes `page`/`pageSize`)
- Verifies `getDetail(id)` constructs the correct URL
- Total: **6 tests, all passing**

---

## Phase 7: Frontend — Request List Component

### Files created

- `src/ClaudeCodeProxyAngular/src/app/components/llm-requests-list/llm-requests-list.ts`
- `src/ClaudeCodeProxyAngular/src/app/components/llm-requests-list/llm-requests-list.html`
- `src/ClaudeCodeProxyAngular/src/app/components/llm-requests-list/llm-requests-list.scss`
- `src/ClaudeCodeProxyAngular/src/app/components/llm-requests-list/llm-requests-list.spec.ts`

### Files modified

- `src/ClaudeCodeProxyAngular/src/app/app.ts`
- `src/ClaudeCodeProxyAngular/src/app/app.html`

### Step-by-step

**Step 1 — Reviewed `HourlyStats` and `DailyStats`** to understand the established component patterns:
- Standalone components with `implements OnInit`
- `signal<T>()` for `data`, `loading`, `error` reactive state
- Angular 17+ `@if`/`@for` control flow in templates
- Loading/error/empty state pattern with shared CSS classes from `styles.scss`

**Step 2 — Created `llm-requests-list.ts`:**
- `@Output() requestSelected = new EventEmitter<number>()` — emits the request `id` on row click
- `ngOnInit` calls `requestsService.getRecent()` with no arguments (backend defaults to last 24 hours)
- Three helper methods used in the template:
  - `formatDuration(ms)` — returns `"456ms"` for sub-second, `"1.2s"` for longer
  - `truncatePath(path)` — truncates to 60 chars with `…` suffix
  - `statusClass(code)` — returns `"status-ok"` (2xx) or `"status-err"` (4xx/5xx)

**Step 3 — Created `llm-requests-list.html`:**
- Reuses shared `.stats-section`, `.state-message`, `.table-wrapper` CSS classes from `styles.scss`
- Table columns: Time (UTC `dd MMM HH:mm:ss`), Model, Path, Status, Duration, Input Tokens, Output Tokens
- Null fields render as `—` using the `??` operator and ternary for tokens
- `(click)="requestSelected.emit(req.id)"` on each `<tr>`; `.clickable` class sets `cursor: pointer`

**Step 4 — Created `llm-requests-list.scss`:**
- `.clickable` — `cursor: pointer`
- `.status-ok` — green (`#4caf82`)
- `.status-err` — `var(--color-error)`
- `.path-cell` — monospace font, `text-overflow: ellipsis`, `max-width: 300px`

**Step 5 — Updated `AppComponent` (Task 7.2):**
- `app.ts`: added `import { signal }` and `LlmRequestsList` import; added `readonly selectedRequestId = signal<number | null>(null)` to the class
- `app.html`: added `<app-llm-requests-list (requestSelected)="selectedRequestId.set($event)" />` as the first element inside `<main>`, above the hourly and daily stats sections

**Step 6 — Created `llm-requests-list.spec.ts`:**
- Follows `HourlyStats` spec pattern: `jest.fn()` mock service, `render()` from `@testing-library/angular`
- Tests loading state, error state, empty state, table headers, row rendering
- Row-click test: uses `jest.spyOn(fixture.componentInstance.requestSelected, 'emit')` then `fireEvent.click(row)` and asserts `emit` was called with the correct id
- Total: **14 tests, all passing**

---

## Phase 8: Frontend — Request Detail Panel Component

### Files created

- `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.ts`
- `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.html`
- `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.scss`
- `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.spec.ts`

### Step-by-step

**Step 1 — Defined private TypeScript interfaces** for Anthropic API shapes (kept private to the component file, not exported):
- `ContentBlock` — `{ type: string; text?: string }`
- `AnthropicMessage` — `{ role: string; content: string | ContentBlock[] }`
- `AnthropicTool` — `{ name: string; description?: string }`
- `AnthropicRequestBody` — model, max_tokens, temperature, system, messages, tools
- `AnthropicResponseBody` — stop_reason, content, usage
- `StreamingView` — the parsed/reconstructed result from SSE lines (model, assistantText, inputTokens, outputTokens, stopReason)

**Step 2 — Implemented `RequestDetailPanel` class:**
- `implements OnChanges` — `ngOnChanges` checks `changes['requestId']` and calls `loadDetail()`, which resets all state signals before fetching
- `@Input({ required: true }) requestId!: number`
- `@Output() closed = new EventEmitter<void>()`
- Reactive state signals: `detail`, `loading`, `error`, `requestViewRaw`, `responseViewRaw`
- **Seven computed signals** to avoid repeated JSON parsing in the template:
  - `parsedRequestBody` — `JSON.parse(requestBody)` → `AnthropicRequestBody | null`
  - `rawRequestBody` — pretty-printed request JSON (2-space indent)
  - `parsedResponseJson` — `JSON.parse(responseBody)` for non-streaming only
  - `parsedStreamingResponse` — calls `buildStreamingView()` for streaming only
  - `rawResponseBody` — raw SSE string for streaming, pretty-printed JSON for non-streaming
  - `requestHeaderEntries` — `Object.entries(JSON.parse(requestHeaders))`
  - `responseHeaderEntries` — same for response headers
- **Private `buildStreamingView(body)`** — splits on `\n`, filters `data: ` lines, parses JSON, walks events:
  - `message_start` → extracts `model` and `inputTokens`
  - `content_block_delta` where `delta.type === "text_delta"` → concatenates `delta.text` to `assistantText`
  - `message_delta` → extracts `outputTokens` and `stopReason`
- **Public template helpers**: `getSystemText()` (handles `string | ContentBlock[]`), `getMessageText()` (same), `statusClass()`, `formatDuration()`

**Step 3 — Created `request-detail-panel.html`** with four sections inside `@if (detail(); as d)`:

- **Panel header** (sticky, `position: sticky; top: 3.5rem`): title + Close button that emits `closed`
- **Section 1 — Metadata**: two-column CSS grid (`auto 1fr`) with labelled rows for time, method+path, status, duration, model, tokens. Status uses `[class]="statusClass(...)"`.
- **Section 2 — Request body**: toggle button switches `requestViewRaw` signal; formatted branch uses `@if (parsedRequestBody(); as parsed)` to render param row, system block, message bubbles (`@for` over `parsed.messages`), and tools `<details>`; raw branch shows `rawRequestBody()` in `<pre class="raw-view">`
- **Section 3 — Response body**: toggle button switches `responseViewRaw`; branches on `d.isStreaming`:
  - Streaming: `@if (parsedStreamingResponse(); as streamed)` renders param row + single assistant message bubble
  - Non-streaming: `@if (parsedResponseJson(); as parsedResp)` renders stop_reason, usage params, and content blocks as assistant bubbles
  - Both branches have a raw fallback `<pre>` for unparseable bodies
- **Section 4 — Headers**: two `<details>`/`<summary>` elements (collapsed by default); each iterates the computed `requestHeaderEntries()` / `responseHeaderEntries()` signals into a `<dl>` grid

**Step 4 — Created `request-detail-panel.scss`:**
- Distinct background per section: metadata (`--color-bg`), request (`#191c29`), response (`#1c1929`), headers (`--color-surface`)
- `.meta-grid`: `grid-template-columns: auto 1fr` for aligned label/value rows
- `.message` / `.message-user` / `.message-assistant`: chat-bubble styling with role-coloured header bar; user uses blue tint (`#1b2040`), assistant uses purple tint (`#1f1b3d`)
- `.raw-view`: dark background (`#090b10`), monospace font, `overflow-x: auto`, `max-height: 60vh` with vertical scroll
- `.tools-list`: styled `<details>` with custom `▶`/`▼` marker via `::before` pseudo-element
- `.close-btn` in `.panel-header` with `position: sticky; top: 3.5rem; z-index: 5`

**Step 5 — Created `request-detail-panel.spec.ts`:**
- Uses `componentInputs: { requestId }` in `render()` to set the required input
- Uses `rerender({ componentInputs: { requestId: 2 } })` to test `ngOnChanges` re-fetch
- Output event test: `jest.spyOn(fixture.componentInstance.closed, 'emit')` then `fireEvent.click(screen.getByText(/Close/))`
- Raw view toggle tests: `screen.getAllByText('Raw')` selects by index (index 0 = request, index 1 = response); asserts `<pre class="raw-view">` text content via `container.querySelector`
- Streaming test data uses a multi-line SSE string constant (`SSE_BODY`) with `message_start`, two `content_block_delta`, and `message_delta` events
- One test fix during development: the headers test initially used `getByText('content-type')` which found two elements (one in each section) — corrected to `getAllByText('content-type').length >= 1`
- Total: **29 tests, all passing**

---

## Key design decisions across phases 6–8

**Computed signals over template method calls** — In `RequestDetailPanel`, all JSON parsing and header extraction is done in `computed()` signals rather than raw method calls in the template. This avoids re-parsing on every change detection cycle and makes the parsed results easily testable via the component instance.

**`ngOnChanges` over `ngOnInit`** — `RequestDetailPanel` implements `OnChanges` rather than `OnInit` so that it re-fetches automatically when the parent changes `requestId` without destroying and recreating the component.

**`@if (signal(); as local)`** — Used throughout `request-detail-panel.html` to bind computed signal values to local template variables (e.g. `@if (parsedRequestBody(); as parsed)`). This is the Angular 17+ control-flow equivalent of the `*ngIf="x as y"` pattern and avoids calling the signal getter multiple times per template block.

**No third-party dependencies added** — All JSON parsing and SSE reconstruction uses native `JSON.parse` and string splitting. The header collapsible sections use the browser-native `<details>`/`<summary>` elements rather than any Angular or third-party accordion component.

**Mock service pattern** — Tests provide the service as a plain object mock (`{ getDetail: jest.fn() }`) via `providers: [{ provide: RequestsService, useValue: mockRequestsService }]`, consistent with the existing `HourlyStats` and `DailyStats` spec patterns.
