# Phase 2 Extra — Steps Taken (Unit Tests for ProxyMiddleware)

A chronological record of every action performed to add Task 2.6 (unit tests for `ProxyMiddleware`) as an extension to Phase 2.

---

## 1. Updated the implementation plan

Added **Task 2.6 — Unit Tests for ProxyMiddleware** to `docs/ImplementationPlan-v1.md` immediately after Task 2.5. The task describes nine test scenarios:

- Basic GET forwarding (status and body)
- POST with body and Content-Type forwarding
- Custom header forwarding (`x-api-key`, `anthropic-version`)
- Hop-by-hop header stripping from the request
- Response header forwarding from upstream
- `Content-Length` stripping from the response
- SSE streaming response forwarding
- 502 on upstream connection failure
- 504 on upstream timeout

`RichardSzalay.MockHttp` was selected as the HTTP mocking library (as suggested). It is purpose-built for mocking `HttpClient` and provides a clean fluent API for setting up expected requests and canned responses, including support for lambda-based response factories and throwing exceptions — all needed for these tests.

---

## 2. Created the NUnit test project

```bash
dotnet new nunit --name ClaudeCodeProxy.Tests --framework net10.0
```

The template generated:
- `ClaudeCodeProxy.Tests.csproj` referencing NUnit 4.3.2, NUnit3TestAdapter 5.0.0, and `Microsoft.NET.Test.Sdk`
- A global implicit `using NUnit.Framework;` in the project properties
- A placeholder `UnitTest1.cs`

```bash
dotnet sln add ClaudeCodeProxy.Tests/ClaudeCodeProxy.Tests.csproj
```

---

## 3. Added packages and project reference

```bash
cd ClaudeCodeProxy.Tests
dotnet add package RichardSzalay.MockHttp      # 7.0.0 — HTTP mocking
dotnet add reference ../ClaudeCodeProxy/ClaudeCodeProxy.csproj
```

`RichardSzalay.MockHttp` 7.0.0 targets `netstandard2.0` and is compatible with .NET 10.

The project reference gives the test project access to `ProxyMiddleware` and `UpstreamOptions` without needing to duplicate or stub them.

---

## 4. Removed the template placeholder

```bash
rm ClaudeCodeProxy.Tests/UnitTest1.cs
```

---

## 5. Created the test file

Created `ClaudeCodeProxy.Tests/Middleware/ProxyMiddlewareTests.cs`.

### Helper structure

**`CreateMiddleware(HttpClient)`** — constructs a `ProxyMiddleware` instance with:
- A `TestHttpClientFactory` (inner class) that wraps the provided `HttpClient` — avoids adding a general-purpose mocking library like NSubstitute/Moq just for one interface
- `NullLogger<ProxyMiddleware>.Instance` from `Microsoft.Extensions.Logging.Abstractions` (already available transitively)
- `UpstreamOptions` with `BaseUrl = "https://api.anthropic.com"`
- `_ => Task.CompletedTask` as the `RequestDelegate` — required by the ASP.NET Core middleware convention but unused since `ProxyMiddleware` is terminal

**`CreateContext(method, path, body, headers)`** — creates a `DefaultHttpContext` with:
- `Response.Body` replaced with a `MemoryStream` (the default `Stream.Null` is write-only and not readable after the fact)
- `Request.Body` set to either an empty `MemoryStream` or one wrapping the UTF-8 encoded body string

**`ReadResponseBodyAsync(context)`** — resets `Response.Body.Position` to 0 and reads the full body as a string.

### Tests and key decisions

| Test | What it verifies | Key technique |
|---|---|---|
| `ForwardsGetRequest_ReturnsUpstreamStatusAndBody` | Status code and body are returned from upstream | `MockHttpMessageHandler.Respond(statusCode, mediaType, content)` |
| `ForwardsPostRequest_BodyAndContentTypeReachUpstream` | Request body and `Content-Type` reach the upstream | Lambda `Respond(async req => ...)` captures and reads `req.Content` |
| `ForwardsCustomRequestHeaders_ToUpstream` | `x-api-key` and `anthropic-version` headers are forwarded | Lambda captures `HttpRequestMessage`; asserts `Headers.Contains(...)` |
| `StripsHopByHopHeaders_FromRequest` | `Connection` and `Host` are absent from the upstream request; `x-api-key` survives | Same capture technique; asserts `.Contains("Connection")` is false |
| `ForwardsResponseHeaders_FromUpstream` | Custom response headers appear on `context.Response.Headers` | Lambda builds `HttpResponseMessage` with `TryAddWithoutValidation` |
| `StripsContentLength_FromResponse` | `Content-Length` is absent from `context.Response.Headers` | `Headers.ContainsKey("Content-Length")` is false |
| `StreamingResponse_IsForwardedAndBodyMatchesUpstream` | `text/event-stream` response triggers the chunk-streaming path; full body arrives | `StringContent` with `"text/event-stream"` media type; asserts `ContentType` and full body |
| `Returns502_WhenUpstreamConnectionFails` | `HttpRequestException` → 502 | `MockedRequest.Throw(new HttpRequestException(...))` |
| `Returns504_WhenUpstreamTimesOut` | `TaskCanceledException` (without `RequestAborted` being cancelled) → 504 | `MockedRequest.Throw(new TaskCanceledException(...))` |

**Why `TaskCanceledException` → 504 works correctly**: `TaskCanceledException` is a subclass of `OperationCanceledException`. The first catch in `ProxyMiddleware` has a `when (requestAborted.IsCancellationRequested)` guard. In tests, `DefaultHttpContext.RequestAborted` is never cancelled, so the guard is false and the exception falls through to the `TaskCanceledException` handler which returns 504. ✓

**Why `Transfer-Encoding` is not asserted in the hop-by-hop test**: `HttpClient` itself handles chunked transfer encoding at the protocol layer and does not expose `Transfer-Encoding: chunked` as a header on `HttpRequestMessage`. Testing for it being absent would always pass regardless of the middleware's behaviour. The test instead asserts on `Connection` (which is explicitly in the middleware's exclusion set and is directly settable on `HttpRequestMessage.Headers`).

---

## 6. Ran the tests

```bash
dotnet test ClaudeCodeProxyDotNet.slnx --logger "console;verbosity=normal"
```

Result:
```
Total tests: 9
     Passed: 9
Total time:  0.6 Seconds
```

All 9 tests passed on the first run.

---

## Files created / modified

| File | Action |
|---|---|
| `docs/ImplementationPlan-v1.md` | Modified — added Task 2.6 |
| `ClaudeCodeProxy.Tests/ClaudeCodeProxy.Tests.csproj` | Created (via template), then modified with `RichardSzalay.MockHttp` package and project reference |
| `ClaudeCodeProxy.Tests/UnitTest1.cs` | Deleted (template placeholder) |
| `ClaudeCodeProxy.Tests/Middleware/ProxyMiddlewareTests.cs` | Created — 9 unit tests |
