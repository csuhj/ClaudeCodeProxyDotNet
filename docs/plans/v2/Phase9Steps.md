# Phase 9 Implementation Steps

## Task 9.1 — Wire `AppComponent` State

### 1. Read the plan and TODO

Read `docs/plans/ImplementationPlan-v2.md` and `docs/plans/v2/TODO.md` to confirm phases 1–8 were complete and Phase 9 was the next uncompleted phase.

### 2. Inspect the current `AppComponent` files

Read `src/ClaudeCodeProxyAngular/src/app/app.ts` and `app.html` to assess what was already in place:

- `app.ts` already had `selectedRequestId = signal<number | null>(null)` but lacked the `onRequestSelected`/`onDetailClosed` handlers and the `RequestDetailPanel` import.
- `app.html` already bound `(requestSelected)` inline to `selectedRequestId.set($event)` but had no conditional rendering of the detail panel.

### 3. Confirm `RequestDetailPanel` was fully implemented

Read `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.ts` to verify the component existed with the correct `@Input() requestId` and `@Output() closed` interface before wiring it into the app shell.

### 4. Update `app.ts`

- Added `import { RequestDetailPanel }` from the detail panel component path.
- Added `RequestDetailPanel` to the `imports` array of the `@Component` decorator.
- Added the `onRequestSelected(id: number)` handler (calls `this.selectedRequestId.set(id)`).
- Added the `onDetailClosed()` handler (calls `this.selectedRequestId.set(null)`).

### 5. Update `app.html`

- Changed the `(requestSelected)` binding from the inline `selectedRequestId.set($event)` to the new `onRequestSelected($event)` handler.
- Added an `@if (selectedRequestId() !== null)` block immediately after the list component, rendering `<app-request-detail-panel>` with `[requestId]="selectedRequestId()!"` and `(closed)="onDetailClosed()"`.
- Kept the existing `<app-hourly-stats />` and `<app-daily-stats />` below the new block, matching the layout order from the plan.

### 6. Build the Angular project

Ran `npm run build` from `src/ClaudeCodeProxyAngular/` to confirm the production build completed without errors. A CSS budget warning on `request-detail-panel.scss` (4.80 kB vs 4.00 kB limit) was noted — this is a non-blocking warning, not a compilation error.

### 7. Run the .NET tests

Ran `dotnet test` from the solution root to confirm all 94 backend tests still passed after the frontend changes.

### 8. Updated `TODO.md`

Marked Task 9.1 as complete (`[x]`) in `docs/plans/v2/TODO.md`.

---

## Task 9.2 — End-to-End Smoke Test

This task requires a live session and cannot be automated. The manual steps to verify are:

1. Start the .NET backend: `dotnet run --project src/ClaudeCodeProxy`
2. Run a Claude Code session through the proxy to generate LLM request records.
3. Open the dashboard at `http://localhost:5000` (production build) or `http://127.0.0.1:4200` (dev server).
4. Confirm the LLM Requests list shows requests from the last 24 hours.
5. Click a row and confirm the detail panel renders below the list.
6. Confirm the formatted request body shows the system prompt and conversation messages.
7. For a streaming request, confirm the formatted response shows reconstructed assistant text; for a non-streaming request, confirm the JSON content blocks are displayed.
8. Toggle "Raw" on both sections and confirm the underlying JSON or SSE text is shown.
9. Expand the Headers sections and confirm request and response headers are listed.
10. Click "Close" and confirm the detail panel is dismissed and the list is visible again.
