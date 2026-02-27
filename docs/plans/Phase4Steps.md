# Phase 4: LLM Token Counting — Implementation Steps

## Overview

Phase 4 adds extraction and persistence of LLM token usage from Anthropic Messages API calls recorded by the proxy. It covers both non-streaming (JSON) and streaming (SSE / `text/event-stream`) responses.

---

## Step 1 — Create `TokenUsageResult` DTO

**File:** `src/ClaudeCodeProxy/Models/TokenUsageResult.cs`

A simple data-transfer object returned by the token parser:

| Property | Type | Notes |
|---|---|---|
| `Model` | `string?` | Model name extracted from the response |
| `InputTokens` | `int` | `usage.input_tokens` |
| `OutputTokens` | `int` | `usage.output_tokens` |
| `CacheReadTokens` | `int` | `usage.cache_read_input_tokens` (0 if absent) |
| `CacheCreationTokens` | `int` | `usage.cache_creation_input_tokens` (0 if absent) |

This is a separate DTO rather than re-using `LlmUsage` so the parser has no EF Core dependency.

---

## Step 2 — Implement `TokenUsageParser`

**File:** `src/ClaudeCodeProxy/Services/TokenUsageParser.cs`

A `static` class with three public members:

### `IsAnthropicMessagesCall(string path, string method)`

Detects whether a recorded request is an Anthropic Messages API call:
- Method must be `POST` (case-insensitive).
- Path (query string stripped before comparison) must end with `/v1/messages` or `/messages` to accommodate arbitrary path prefixes.

Rationale for stripping the query string: `ProxyRequest.Path` stores the full path-and-query string, so a call like `/v1/messages?stream=true` would otherwise fail a plain `EndsWith` check.

### `ParseNonStreaming(string? responseBody)`

Parses a standard (non-streaming) Anthropic Messages API JSON response:
- Uses `System.Text.Json.JsonDocument` to navigate the document.
- Reads `model` from the top-level `model` property.
- Reads token counts from the top-level `usage` object.
- Returns `null` for null/empty input or any `JsonException`.

### `ParseStreaming(string? responseBody)`

Parses an SSE response body (the accumulated `text/event-stream` body captured by the proxy middleware):
- Splits on newlines and processes each `data:` line independently.
- Parses each `data:` payload as JSON; malformed lines are skipped silently.
- **`message_start`** event: extracts `message.model` and `message.usage` (initial token counts, output is 0 at this point).
- **`message_delta`** event: extracts `usage` with final cumulative token counts including `output_tokens`. These values take precedence over `message_start` counts for all token fields.
- Model name always comes from `message_start` (it does not appear in `message_delta`).
- Falls back to `message_start` data if no `message_delta` event is present.
- Returns `null` if neither relevant event is found or input is null/empty.

#### SSE token event structure (from sample data)

`message_start` carries initial counts:
```json
{
  "type": "message_start",
  "message": {
    "model": "claude-sonnet-4-6",
    "usage": {
      "input_tokens": 3,
      "cache_creation_input_tokens": 1886,
      "cache_read_input_tokens": 18685,
      "output_tokens": 0
    }
  }
}
```

`message_delta` carries final cumulative counts (including output tokens):
```json
{
  "type": "message_delta",
  "delta": { "stop_reason": "end_turn" },
  "usage": {
    "input_tokens": 3,
    "cache_creation_input_tokens": 1886,
    "cache_read_input_tokens": 18685,
    "output_tokens": 176
  }
}
```

---

## Step 3 — Update `RecordingService`

**File:** `src/ClaudeCodeProxy/Services/RecordingService.cs`

`RecordCoreAsync` was updated to perform token extraction before persisting the `ProxyRequest`:

1. Call `TokenUsageParser.IsAnthropicMessagesCall(request.Path, request.Method)`.
2. If true, determine whether the response was streaming by parsing `request.ResponseHeaders` JSON and checking `Content-Type` for `text/event-stream`.
3. Call the appropriate parser (`ParseStreaming` or `ParseNonStreaming`).
4. If a `TokenUsageResult` is returned, populate `request.LlmUsage` (the existing one-to-one navigation property on `ProxyRequest`) with a new `LlmUsage` entity.
5. EF Core cascade-inserts the `LlmUsage` row in the same `SaveChangesAsync` call as the `ProxyRequest`, preserving atomicity.
6. Log a warning if parsing returns `null` for a recognised LLM call path.

A private helper `IsStreamingResponse(string responseHeadersJson)` was added to check the JSON-serialised response headers for `Content-Type: text/event-stream`.

---

## Step 4 — Unit Tests

**File:** `test/ClaudeCodeProxy.Tests/Services/TokenUsageParserTests.cs`

24 tests covering:

### `IsAnthropicMessagesCall`
- `POST /v1/messages` → `true`
- `POST /v1/messages?stream=true` → `true` (query string stripped)
- `POST /prefix/v1/messages` → `true` (path prefix tolerated)
- `POST /messages` → `true`
- `POST /api/messages` → `true`
- `GET /v1/messages` → `false` (wrong method)
- `POST /v1/other` → `false` (wrong path)
- `POST /v1/messages-extended` → `false` (suffix mismatch)
- `POST ""` → `false` (empty path)
- `get /v1/messages` → `false` (lowercase wrong method)

### `ParseNonStreaming`
- Full response with all token fields → correct values extracted
- Response missing cache fields → defaults to 0
- Response with no `usage` property → `null`
- `null` body → `null`
- Empty / whitespace body → `null`
- Malformed JSON → `null`

### `ParseStreaming`
- Full valid SSE body (matching sample data format) → correct model, input, output, cache tokens
- `message_delta` tokens take precedence over `message_start` tokens
- No `message_delta` present → falls back to `message_start` data
- Model always comes from `message_start` even when `message_delta` is present
- `null` body → `null`
- Empty / whitespace body → `null`
- Body with no relevant events (only `ping`) → `null`
- Malformed `data:` lines skipped gracefully; valid lines still parsed

---

## Files Created / Modified

| Action | File |
|---|---|
| Created | `src/ClaudeCodeProxy/Models/TokenUsageResult.cs` |
| Created | `src/ClaudeCodeProxy/Services/TokenUsageParser.cs` |
| Modified | `src/ClaudeCodeProxy/Services/RecordingService.cs` |
| Created | `test/ClaudeCodeProxy.Tests/Services/TokenUsageParserTests.cs` |

---

## Test Results

All 40 tests pass after Phase 4 implementation (`dotnet test`):
- 16 pre-existing tests (Phase 2 & 3) — all still green
- 24 new `TokenUsageParserTests` — all green
