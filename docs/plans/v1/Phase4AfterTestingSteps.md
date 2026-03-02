# Phase 4 — Post-Testing Fix: Gzip-Encoded Response Handling

## Problem Discovered During Manual Testing

When Claude Code is pointed at the proxy, Anthropic's API sometimes returns responses with `Content-Encoding: gzip`. The proxy was configured with `AutomaticDecompression = System.Net.DecompressionMethods.None` so that compressed responses are forwarded to the client as-is (preserving the original `Content-Encoding` header so the client can decompress). However, the proxy was also passing those raw gzip bytes directly to the recording service, which then tried to interpret them as UTF-8 text. The result was either garbage or an exception, meaning:

- `ProxyRequest.ResponseBody` was stored as corrupted text
- Token parsing (`TokenUsageParser`) received unparseable input and silently returned `null`
- No `LlmUsage` row was saved for affected calls

## Fix

The decompression needs to happen at the point where the recording service reads the captured bytes as a string — not at the point of forwarding to the client.

### Step 1 — Write a failing test

Two tests were added to `ProxyMiddlewareTests` to pin the expected behaviour:

**`GzippedResponse_ForwardsCompressedBytesToClient`**
Verifies the existing (correct) behaviour: the raw compressed bytes and the `Content-Encoding: gzip` header are forwarded to the client unchanged.

**`GzippedResponse_RecordingService_ReceivesDecompressedBody`**
Verifies the new (required) behaviour: even though the client receives compressed bytes, the recording service receives the plain-text decompressed body. This test failed before the fix.

A `GzipCompress(string text)` helper was added to the test class to produce compressed byte arrays from plain-text strings.

### Step 2 — Fix `ProxyMiddleware`

**File:** `src/ClaudeCodeProxy/Middleware/ProxyMiddleware.cs`

Two changes were made:

1. Added `using System.IO.Compression;`

2. Replaced the call to `ReadAsStringAsync(responseBodyMs)` in `RecordRequestAsync` with a call to a new `DecodeResponseBodyAsync` method:

```csharp
responseBodyMs.Position = 0;
var responseBodyText = responseBodyMs.Length > 0
    ? await DecodeResponseBodyAsync(responseBodyMs, upstreamResponse.Content.Headers.ContentEncoding)
    : null;
```

3. Added the new helper method:

```csharp
private static async Task<string> DecodeResponseBodyAsync(
    MemoryStream ms, ICollection<string> contentEncodings)
{
    if (contentEncodings.Contains("gzip", StringComparer.OrdinalIgnoreCase))
    {
        await using var gzip = new GZipStream(ms, CompressionMode.Decompress, leaveOpen: true);
        using var decompressed = new MemoryStream();
        await gzip.CopyToAsync(decompressed);
        decompressed.Position = 0;
        return await ReadAsStringAsync(decompressed);
    }

    return await ReadAsStringAsync(ms);
}
```

The check uses `ContentEncoding` from the `HttpContentHeaders` of the upstream response — this is the same collection already being forwarded to the client as a response header. The `leaveOpen: true` flag is used on the `GZipStream` so the underlying `MemoryStream` is not disposed prematurely.

The compressed bytes written to the client response stream in `WriteResponseBodyAsync` are not affected — decompression only happens on the in-memory copy passed to the recording service.

### Step 3 — Confirm the fix

Running `dotnet test --filter "GzippedResponse"` showed both tests passing. The full suite of 54 tests continued to pass.

## Files Changed

| Action | File |
|---|---|
| Modified | `src/ClaudeCodeProxy/Middleware/ProxyMiddleware.cs` |
| Modified | `test/ClaudeCodeProxy.Tests/Middleware/ProxyMiddlewareTests.cs` |
