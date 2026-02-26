# Phase 2 — Steps Taken

A chronological record of every action performed to complete Phase 2 of the implementation plan.

---

## 1. Read existing project files

Read the following files to understand the current state before making any changes:
- `ClaudeCodeProxy/appsettings.json` — confirmed it contained only `Logging` and `AllowedHosts`
- `ClaudeCodeProxy/Program.cs` — confirmed it was the minimal stub from Phase 1
- `ClaudeCodeProxy/Properties/launchSettings.json` — noted the default HTTP port is `5051`

---

## 2. Created `Models/UpstreamOptions.cs` (Task 2.1)

Created `ClaudeCodeProxy/Models/UpstreamOptions.cs` with two properties:

- `BaseUrl` (`string`) — the upstream URL to proxy to, e.g. `https://api.anthropic.com`
- `TimeoutSeconds` (`int`, default `300`) — maximum time to wait for the upstream to respond; 5 minutes to accommodate long LLM streaming responses

A `SectionName` constant (`"Upstream"`) is defined on the class to be used as the configuration key everywhere, avoiding magic strings.

---

## 3. Updated `appsettings.json` (Task 2.1)

Added an `Upstream` section to `ClaudeCodeProxy/appsettings.json` with defaults:

```json
"Upstream": {
  "BaseUrl": "https://api.anthropic.com",
  "TimeoutSeconds": 300
}
```

`https://api.anthropic.com` is the default Anthropic API base URL. The `ANTHROPIC_BASE_URL` environment variable (handled in `Program.cs`) overrides this at runtime.

---

## 4. Implemented `Middleware/ProxyMiddleware.cs` (Task 2.3)

Created `ClaudeCodeProxy/Middleware/ProxyMiddleware.cs`. Key design decisions:

### Hop-by-hop header sets
Two separate `HashSet<string>` constants define which headers to strip on the way in and on the way out:

- **Request hop-by-hop** — standard RFC 2616 set (`Connection`, `Keep-Alive`, `Proxy-*`, `TE`, `Trailers`, `Transfer-Encoding`, `Upgrade`) plus `Host` (reset by `HttpClient` to the upstream hostname) and `Content-Length` (recalculated from the buffered body)
- **Response hop-by-hop** — same set plus `Content-Length` (stripped so Kestrel sets it based on what is actually written, avoiding mismatches)

### Constructor
The middleware follows the ASP.NET Core convention: `RequestDelegate` is the first constructor parameter (required by `UseMiddleware<T>` registration) but is unused because this is a terminal middleware — no `_next` call. `IHttpClientFactory`, `ILogger<ProxyMiddleware>`, and `UpstreamOptions` are resolved from DI.

### InvokeAsync flow

**Step 1 — Buffer the request body**
The full request body is read into a `MemoryStream`. This serves two purposes: the stream can be replayed for the upstream request, and the buffer will be handed to the recording service in Phase 3.

**Step 2 — Build the upstream request**
The upstream URI is assembled as `_upstreamBaseUrl + path + queryString`. An `HttpRequestMessage` is created with the same HTTP method. If the body buffer is non-empty, a `StreamContent` is attached. Request headers are then copied, skipping hop-by-hop headers. Because `TryAddWithoutValidation` returns `false` for content headers (e.g. `Content-Type`) when called on `HttpRequestMessage.Headers`, those fall through to `upstreamRequest.Content.Headers` automatically.

**Step 3 — Send to upstream**
`HttpClient.SendAsync` is called with `HttpCompletionOption.ResponseHeadersRead` so that the task completes as soon as response headers arrive, enabling body streaming. Three exception cases are handled:
- `OperationCanceledException` when `RequestAborted` is cancelled — client disconnected; return silently
- `TaskCanceledException` — upstream timed out; respond `504 Gateway Timeout`
- `HttpRequestException` — upstream unreachable; respond `502 Bad Gateway`

**Step 4 — Copy response headers**
The response status code is forwarded. Both `upstreamResponse.Headers` (standard headers) and `upstreamResponse.Content.Headers` (content headers such as `Content-Type` and `Content-Encoding`) are copied, skipping hop-by-hop headers.

**Step 5 — Stream or buffer the response body**
Whether the response is a streaming SSE response is detected by checking `Content-Type: text/event-stream`.

- **Streaming path**: a 4 KB read buffer reads chunks from the upstream stream, immediately writing each chunk to `context.Response.Body` and flushing, while also writing to `responseBodyMs`. This gives the client real-time SSE delivery.
- **Buffered path**: the full upstream body is read into `responseBodyMs`, then written to the client in one shot.

An inner `OperationCanceledException` catch handles mid-stream client disconnections silently.

**Step 6 — TODO placeholder for Phase 3**
A comment marks the point at which the recording service will be called, listing the captured data (`requestBodyMs`, `responseBodyMs`, status code, elapsed milliseconds, `isStreaming` flag).

A structured log line records every proxied request: method, path, status code, duration, and streaming mode.

### Why `AutomaticDecompression = None`
Disabling automatic decompression in `HttpClientHandler` means compressed upstream responses (e.g. `Content-Encoding: gzip`) are forwarded as-is. This preserves the accuracy of the `Content-Encoding` header so the client can decompress correctly, and avoids a mismatch where the upstream `Content-Length` (compressed size) wouldn't match the decompressed body written to the client.

---

## 5. Updated `Program.cs` (Tasks 2.1, 2.2, 2.4)

Updated `ClaudeCodeProxy/Program.cs` to:

1. **Read `ANTHROPIC_BASE_URL` env var** and inject it into the configuration system via `builder.Configuration["Upstream:BaseUrl"]`. This is done before options binding so that `IOptions<T>` and direct binding both see the override.

2. **Bind and validate `UpstreamOptions`** — binds from the `"Upstream"` config section, then checks `BaseUrl` is non-empty. Throws `InvalidOperationException` at startup (fail fast) if it is not set.

3. **Register `UpstreamOptions` as a singleton** — registered directly (not as `IOptions<T>`) so `ProxyMiddleware` can receive it via constructor injection without the `IOptions<>` wrapper.

4. **Register the named `HttpClient`** (`"upstream"`) with:
   - `client.Timeout` set from `UpstreamOptions.TimeoutSeconds`
   - `HttpClientHandler` configured with `AllowAutoRedirect = false` and `AutomaticDecompression = None`

5. **Register `ProxyMiddleware`** via `app.UseMiddleware<ProxyMiddleware>()` as the sole terminal handler in the pipeline.

---

## 6. Build verification

```bash
dotnet build ClaudeCodeProxyDotNet.slnx
```

Result: build succeeded, 0 warnings, 0 errors.

---

## 7. Smoke test guidance (Task 2.5)

A live smoke test requires a real Anthropic API key and is therefore not automated here. To perform one manually:

```bash
# Terminal 1 — start the proxy
ANTHROPIC_BASE_URL=https://api.anthropic.com dotnet run --project ClaudeCodeProxy

# Terminal 2 — send a test request through the proxy
curl -i http://localhost:5051/v1/models \
  -H "x-api-key: $ANTHROPIC_API_KEY" \
  -H "anthropic-version: 2023-06-01"
```

A successful proxy will return the upstream Anthropic response with its original status code and headers. The structured log line in Terminal 1 will show:

```
GET /v1/models -> 200 (142ms, buffered)
```

---

## Files created / modified

| File | Action |
|---|---|
| `ClaudeCodeProxy/Models/UpstreamOptions.cs` | Created |
| `ClaudeCodeProxy/Middleware/ProxyMiddleware.cs` | Created |
| `ClaudeCodeProxy/appsettings.json` | Modified — added `Upstream` section |
| `ClaudeCodeProxy/Program.cs` | Modified — added options, HttpClient, middleware registration |
