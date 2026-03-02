# Phase 9 Post-Design Review Steps

Steps taken to action the review comments in `DesignReviewAfterPhase9.md`.

---

## Task 1: Request Detail as a full-screen popup modal

**Goal:** Replace the inline `Request Detail` section with a fixed modal dialog that covers ~95% of the browser window, closing when the ✕ button or backdrop is clicked.

### `src/ClaudeCodeProxyAngular/src/app/app.html`
- Moved `<app-request-detail-panel>` outside `<main>` so it is not constrained by the main content's `max-width` layout.

### `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.html`
- Wrapped the entire panel in a new `<div class="modal-overlay">` that listens for `(click)="onOverlayClick($event)"`, allowing the backdrop to close the dialog.

### `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.ts`
- Added `onOverlayClick(event: MouseEvent)` method: emits `closed` only when the click target is the overlay itself (not a child element), so clicks inside the panel do not close it.

### `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.scss`
- Added `.modal-overlay`: `position: fixed; inset: 0; z-index: 100` with a semi-transparent dark backdrop and flexbox centering.
- Updated `.detail-panel`: `width: 95vw; height: 92vh; max-width: 1400px; display: flex; flex-direction: column; overflow-y: auto` — gives it the large dialog dimensions.
- Changed `.panel-header` sticky offset from `top: 3.5rem` to `top: 0`, since the header now sticks within the scrolling modal rather than the page.

---

## Task 2: Expand/collapse System, User, and Assistant sections

**Goal:** Allow each System prompt block and each message bubble to collapse/expand when its header label is clicked.

### `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.html`
- Replaced `<div class="system-block">` with `<details class="system-block" open>` and its `<div class="block-label">` with `<summary class="block-label">`.
- Replaced every `<div class="message message-X">` (in the request messages loop, the streaming response assistant block, and the non-streaming response content blocks) with `<details class="message message-X" open>`, and each `<div class="message-role">` with `<summary class="message-role">`.
- All blocks start in the `open` (expanded) state.

### `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.scss`
- Added `cursor: pointer; user-select: none; list-style: none; &::-webkit-details-marker { display: none }` to both `.block-label` and `.message-role` to make them behave as clickable headers and suppress the browser's default disclosure triangle.
- Added `::before` pseudo-element with `▶` (closed) content to both. Added separate rules `system-block[open] > .block-label::before` and `.message[open] > .message-role::before` to switch to `▼` when open.

---

## Task 3: Display additional message block types (thinking, tool_use, tool_result)

**Goal:** In addition to `text` blocks, render `thinking`, `tool_use`, and `tool_result` content blocks wherever message content appears.

### `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.ts`

**Interface changes:**
- Extended `ContentBlock` with: `thinking?: string`, `id?: string`, `tool_use_id?: string`, `name?: string`, `input?: Record<string, unknown>`, `content?: string | ContentBlock[]`.
- Added `thinkingText?: string` to `StreamingView`.

**Streaming parser (`buildStreamingView`):**
- Added handling for `content_block_delta` events with `delta.type === 'thinking_delta'`, accumulating `thinkingText` in the same way `text_delta` accumulates `assistantText`.
- Returns `thinkingText` (or `undefined` if empty) alongside the existing fields.

**New helper methods:**
- `getContentBlocks(content)` — normalises a message's `content` (which can be a plain string or a `ContentBlock[]`) into a `ContentBlock[]`, wrapping strings in a single `text` block.
- `getInputEntries(input)` — converts a `tool_use` block's `input` object into `[key, value]` pairs for template iteration; non-string values are JSON-stringified.
- `getToolResultText(content)` — extracts displayable text from a `tool_result` block's `content` field, which can be a string, a `ContentBlock[]`, or undefined.

### `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.html`

**Request body messages:**
- Replaced the single `{{ getMessageText(msg.content) }}` call with a `@for` loop over `getContentBlocks(msg.content)`.
- Each block type renders differently inside the existing collapsible `<details>`:
  - `text` → plain `<div>` with the text
  - `thinking` → `.content-block.block-thinking` with a type label and `.block-body`
  - `tool_use` → `.content-block.block-tool-use` showing `id`, `name`, and a `<dl class="tool-input">` grid of input key-value pairs
  - `tool_result` → `.content-block.block-tool-result` showing `tool_use_id` and the result content text

**Streaming response:**
- Added a `message-thinking` collapsible block rendered before the assistant block when `streamed.thinkingText` is present.

**Non-streaming response content blocks:**
- Removed the `@if (block.type === 'text')` filter; the loop now renders all four block types using the same visual patterns as the request side, but as full collapsible `<details class="message message-X">` rows.

### `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.scss`

**New message colour schemes** (added as `&.message-X` variants inside `.message`):
- `message-thinking`: dark blue-grey background, teal label (`#7ecfd4`)
- `message-tool-use`: dark amber background, amber label (`#f9c74f`)
- `message-tool-result`: dark green background, green label (`#90be6d`)

**New inline block styles** (for blocks rendered inside request message content):
- `.content-block` — bordered container with `border-radius` and `overflow: hidden`
- `.block-type-label` — small uppercase label bar with dark background
- `.block-body` — padded text area with `white-space: pre-wrap`
- `.block-thinking / .block-tool-use / .block-tool-result` — colour the type label to match the above message colours
- `.tool-meta-row` — small metadata row for `id`/`name` fields
- `.tool-input` — two-column CSS grid (`dt` key / `dd` value) with a top border separator

---

## Fix: `tool_result` ID property correction

After the initial implementation it was identified that `tool_result` blocks record their associated tool call ID under the property `tool_use_id`, not `id`.

### `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.ts`
- Added `tool_use_id?: string` to the `ContentBlock` interface (alongside the existing `id?: string` which remains for `tool_use` blocks).

### `src/ClaudeCodeProxyAngular/src/app/components/request-detail-panel/request-detail-panel.html`
- Updated both `tool_result` render sites (inline inside request messages, and in the non-streaming response block loop) to read `block.tool_use_id` instead of `block.id`.
