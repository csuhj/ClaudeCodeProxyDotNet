using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ClaudeCodeProxy.Middleware;
using ClaudeCodeProxy.Models;
using ClaudeCodeProxy.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RichardSzalay.MockHttp;

namespace ClaudeCodeProxy.Tests.Middleware;

[TestFixture]
public class ProxyMiddlewareTests
{
    private const string UpstreamBaseUrl = "https://api.anthropic.com";

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a middleware instance with a no-op mock recording service.
    /// Tests that need to assert recording behaviour should call
    /// <see cref="CreateMiddlewareWithRecordingMock"/> instead.
    /// </summary>
    private static ProxyMiddleware CreateMiddleware(HttpClient httpClient)
    {
        var recordingMock = new Mock<IRecordingService>();
        return CreateMiddlewareCore(httpClient, recordingMock.Object);
    }

    /// <summary>
    /// Creates a middleware instance and returns the recording service mock
    /// so tests can assert that <see cref="IRecordingService.Record"/> was called.
    /// </summary>
    private static (ProxyMiddleware Middleware, Mock<IRecordingService> RecordingMock)
        CreateMiddlewareWithRecordingMock(HttpClient httpClient)
    {
        var recordingMock = new Mock<IRecordingService>();
        return (CreateMiddlewareCore(httpClient, recordingMock.Object), recordingMock);
    }

    private static ProxyMiddleware CreateMiddlewareCore(HttpClient httpClient, IRecordingService recordingService)
    {
        var options = new UpstreamOptions { BaseUrl = UpstreamBaseUrl };
        return CreateMiddlewareCore(httpClient, recordingService, options);
    }

    private static ProxyMiddleware CreateMiddlewareCore(
        HttpClient httpClient, IRecordingService recordingService, UpstreamOptions options)
    {
        var factory = new TestHttpClientFactory(httpClient);
        var logger = NullLogger<ProxyMiddleware>.Instance;
        return new ProxyMiddleware(_ => Task.CompletedTask, factory, logger, options, recordingService);
    }

    private static DefaultHttpContext CreateContext(
        string method = "GET",
        string path = "/v1/models",
        string? body = null,
        Dictionary<string, string>? headers = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.Request.Body = body is null
            ? new MemoryStream()
            : new MemoryStream(Encoding.UTF8.GetBytes(body));

        if (headers != null)
            foreach (var (k, v) in headers)
                context.Request.Headers[k] = v;

        return context;
    }

    private static async Task<string> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsGetRequest_ReturnsUpstreamStatusAndBody()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{UpstreamBaseUrl}/v1/models")
            .Respond(HttpStatusCode.OK, "application/json", """{"models":[]}""");

        var middleware = CreateMiddleware(mockHttp.ToHttpClient());
        var context = CreateContext("GET", "/v1/models");

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await ReadResponseBodyAsync(context), Is.EqualTo("""{"models":[]}"""));
    }

    [Test]
    public async Task ForwardsPostRequest_BodyAndContentTypeReachUpstream()
    {
        const string requestBody = """{"model":"claude-opus-4-6","messages":[]}""";
        string? capturedBody = null;
        string? capturedContentType = null;

        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, $"{UpstreamBaseUrl}/v1/messages")
            .Respond(async req =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
                capturedContentType = req.Content!.Headers.ContentType?.MediaType;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"id":"msg_1"}""", Encoding.UTF8, "application/json")
                };
            });

        var middleware = CreateMiddleware(mockHttp.ToHttpClient());
        var context = CreateContext("POST", "/v1/messages", body: requestBody,
            headers: new Dictionary<string, string> { ["Content-Type"] = "application/json" });

        await middleware.InvokeAsync(context);

        Assert.That(capturedBody, Is.EqualTo(requestBody));
        Assert.That(capturedContentType, Is.EqualTo("application/json"));
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task ForwardsCustomRequestHeaders_ToUpstream()
    {
        HttpRequestMessage? capturedRequest = null;

        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{UpstreamBaseUrl}/v1/models")
            .Respond(req =>
            {
                capturedRequest = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("", Encoding.UTF8, "application/json")
                };
            });

        var middleware = CreateMiddleware(mockHttp.ToHttpClient());
        var context = CreateContext(headers: new Dictionary<string, string>
        {
            ["x-api-key"] = "test-key-123",
            ["anthropic-version"] = "2023-06-01"
        });

        await middleware.InvokeAsync(context);

        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.Headers.Contains("x-api-key"), Is.True);
        Assert.That(capturedRequest.Headers.GetValues("x-api-key").First(), Is.EqualTo("test-key-123"));
        Assert.That(capturedRequest.Headers.Contains("anthropic-version"), Is.True);
        Assert.That(capturedRequest.Headers.GetValues("anthropic-version").First(), Is.EqualTo("2023-06-01"));
    }

    [Test]
    public async Task StripsHopByHopHeaders_FromRequest()
    {
        HttpRequestMessage? capturedRequest = null;

        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{UpstreamBaseUrl}/v1/models")
            .Respond(req =>
            {
                capturedRequest = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("", Encoding.UTF8, "application/json")
                };
            });

        var middleware = CreateMiddleware(mockHttp.ToHttpClient());
        var context = CreateContext(headers: new Dictionary<string, string>
        {
            ["Connection"] = "keep-alive",
            ["Transfer-Encoding"] = "chunked",
            ["x-api-key"] = "test-key"          // should survive
        });

        await middleware.InvokeAsync(context);

        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.Headers.Contains("Connection"), Is.False);
        Assert.That(capturedRequest.Headers.Contains("Host"), Is.False);
        Assert.That(capturedRequest.Headers.Contains("x-api-key"), Is.True);
    }

    [Test]
    public async Task ForwardsResponseHeaders_FromUpstream()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{UpstreamBaseUrl}/v1/models")
            .Respond(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("", Encoding.UTF8, "application/json")
                };
                response.Headers.TryAddWithoutValidation("x-request-id", "abc-123");
                response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "999");
                return response;
            });

        var middleware = CreateMiddleware(mockHttp.ToHttpClient());
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.Headers["x-request-id"].ToString(), Is.EqualTo("abc-123"));
        Assert.That(context.Response.Headers["x-ratelimit-remaining"].ToString(), Is.EqualTo("999"));
    }

    [Test]
    public async Task StripsContentLength_FromResponse()
    {
        // Content-Length is in the hop-by-hop response set so Kestrel can
        // set it correctly based on what is actually written.
        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{UpstreamBaseUrl}/v1/models")
            .Respond(HttpStatusCode.OK, "application/json", """{"ok":true}""");

        var middleware = CreateMiddleware(mockHttp.ToHttpClient());
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.Headers.ContainsKey("Content-Length"), Is.False);
    }

    [Test]
    public async Task StreamingResponse_IsForwardedAndBodyMatchesUpstream()
    {
        const string sseData =
            "data: {\"type\":\"message_start\"}\n\n" +
            "data: {\"type\":\"content_block_delta\",\"delta\":{\"text\":\"Hello\"}}\n\n" +
            "data: {\"type\":\"message_stop\"}\n\n";

        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, $"{UpstreamBaseUrl}/v1/messages")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sseData, Encoding.UTF8, "text/event-stream")
            });

        var middleware = CreateMiddleware(mockHttp.ToHttpClient());
        var context = CreateContext("POST", "/v1/messages");

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        // Content-Type should include text/event-stream
        Assert.That(context.Response.ContentType, Does.Contain("text/event-stream"));
        // Full SSE body should have been forwarded
        Assert.That(await ReadResponseBodyAsync(context), Is.EqualTo(sseData));
    }

    [Test]
    public async Task Returns502_WhenUpstreamConnectionFails()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{UpstreamBaseUrl}/v1/models")
            .Throw(new HttpRequestException("Connection refused"));

        var middleware = CreateMiddleware(mockHttp.ToHttpClient());
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(502));
    }

    [Test]
    public async Task Returns504_WhenUpstreamTimesOut()
    {
        // TaskCanceledException without RequestAborted being cancelled = timeout
        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{UpstreamBaseUrl}/v1/models")
            .Throw(new TaskCanceledException("Upstream timed out"));

        var middleware = CreateMiddleware(mockHttp.ToHttpClient());
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(504));
    }

    // ── Recording tests ───────────────────────────────────────────────────────

    [Test]
    public async Task RecordingService_IsCalledAfterSuccessfulRequest()
    {
        const string responseBody = """{"id":"msg_1","type":"message"}""";

        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, $"{UpstreamBaseUrl}/v1/messages")
            .Respond(HttpStatusCode.OK, "application/json", responseBody);

        var (middleware, recordingMock) = CreateMiddlewareWithRecordingMock(mockHttp.ToHttpClient());
        var context = CreateContext("POST", "/v1/messages", body: """{"model":"claude-opus-4-6"}""",
            headers: new Dictionary<string, string> { ["Content-Type"] = "application/json" });

        await middleware.InvokeAsync(context);

        recordingMock.Verify(
            r => r.Record(It.Is<ProxyRequest>(req =>
                req.Method == "POST" &&
                req.Path == "/v1/messages" &&
                req.ResponseStatusCode == 200 &&
                req.ResponseBody == responseBody &&
                req.DurationMs >= 0)),
            Times.Once);
    }

    [Test]
    public async Task RecordingService_IsNotCalledOnUpstreamConnectionFailure()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{UpstreamBaseUrl}/v1/models")
            .Throw(new HttpRequestException("Connection refused"));

        var (middleware, recordingMock) = CreateMiddlewareWithRecordingMock(mockHttp.ToHttpClient());
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        recordingMock.Verify(r => r.Record(It.IsAny<ProxyRequest>()), Times.Never);
    }

    [Test]
    public async Task RecordingService_CapturesRequestBody()
    {
        const string requestBody = """{"model":"claude-opus-4-6","messages":[]}""";

        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, $"{UpstreamBaseUrl}/v1/messages")
            .Respond(HttpStatusCode.OK, "application/json", """{"id":"msg_1"}""");

        var (middleware, recordingMock) = CreateMiddlewareWithRecordingMock(mockHttp.ToHttpClient());
        var context = CreateContext("POST", "/v1/messages", body: requestBody,
            headers: new Dictionary<string, string> { ["Content-Type"] = "application/json" });

        await middleware.InvokeAsync(context);

        recordingMock.Verify(
            r => r.Record(It.Is<ProxyRequest>(req => req.RequestBody == requestBody)),
            Times.Once);
    }

    // ── Gzip tests ────────────────────────────────────────────────────────────

    [Test]
    public async Task GzippedResponse_ForwardsCompressedBytesToClient()
    {
        // The compressed bytes should reach the client unchanged so the client
        // can decompress them using the Content-Encoding: gzip header it receives.
        const string originalBody = """{"id":"msg_1","type":"message"}""";
        var compressedBytes = GzipCompress(originalBody);

        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, $"{UpstreamBaseUrl}/v1/messages")
            .Respond(_ =>
            {
                var content = new ByteArrayContent(compressedBytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                content.Headers.ContentEncoding.Add("gzip");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            });

        var middleware = CreateMiddleware(mockHttp.ToHttpClient());
        var context = CreateContext("POST", "/v1/messages");

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var rawBytes = ((MemoryStream)context.Response.Body).ToArray();
        Assert.That(rawBytes, Is.EqualTo(compressedBytes));
        Assert.That(context.Response.Headers["Content-Encoding"].ToString(), Is.EqualTo("gzip"));
    }

    [Test]
    public async Task GzippedResponse_RecordingService_ReceivesDecompressedBody()
    {
        // Even though the client receives compressed bytes, the recording service
        // should receive the human-readable decompressed text so token parsing works.
        const string originalBody = """{"id":"msg_1","type":"message"}""";
        var compressedBytes = GzipCompress(originalBody);

        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, $"{UpstreamBaseUrl}/v1/messages")
            .Respond(_ =>
            {
                var content = new ByteArrayContent(compressedBytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                content.Headers.ContentEncoding.Add("gzip");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            });

        var (middleware, recordingMock) = CreateMiddlewareWithRecordingMock(mockHttp.ToHttpClient());
        var context = CreateContext("POST", "/v1/messages");

        await middleware.InvokeAsync(context);

        recordingMock.Verify(
            r => r.Record(It.Is<ProxyRequest>(req => req.ResponseBody == originalBody)),
            Times.Once);
    }

    private static byte[] GzipCompress(string text)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            gz.Write(bytes);
        }
        return ms.ToArray();
    }

    // ── TruncateForStorage unit tests ─────────────────────────────────────────

    [Test]
    public void TruncateForStorage_NullBody_ReturnsNull()
    {
        var result = ProxyMiddleware.TruncateForStorage(null, 100);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TruncateForStorage_BodyWithinLimit_ReturnsUnchanged()
    {
        const string body = "Hello, World!";
        var result = ProxyMiddleware.TruncateForStorage(body, 1000);
        Assert.That(result, Is.EqualTo(body));
    }

    [Test]
    public void TruncateForStorage_BodyExceedsLimit_ReturnsTruncatedWithNote()
    {
        // Build a body that is definitely over 20 bytes when UTF-8 encoded.
        const string body = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";  // 26 bytes
        var result = ProxyMiddleware.TruncateForStorage(body, 10);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.StartWith("ABCDEFGHIJ"));
        Assert.That(result, Does.Contain("[TRUNCATED:"));
        Assert.That(result, Does.Contain("26 bytes"));
        Assert.That(result, Does.Contain("10 bytes"));
    }

    [Test]
    public void TruncateForStorage_EmptyBody_ReturnsEmpty()
    {
        var result = ProxyMiddleware.TruncateForStorage("", 100);
        Assert.That(result, Is.EqualTo(""));
    }

    // ── Body-truncation integration tests ────────────────────────────────────

    [Test]
    public async Task RecordingService_ResponseBodyExceedsLimit_ReceivesTruncatedBody()
    {
        // A response body larger than the configured limit should be truncated
        // before being handed to the recording service.
        var longBody = new string('X', 200);  // 200 chars / bytes

        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{UpstreamBaseUrl}/v1/models")
            .Respond(HttpStatusCode.OK, "application/json", longBody);

        var recordingMock = new Mock<IRecordingService>();
        var options = new UpstreamOptions { BaseUrl = UpstreamBaseUrl, MaxStoredBodyBytes = 50 };
        var middleware = CreateMiddlewareCore(mockHttp.ToHttpClient(), recordingMock.Object, options);
        var context = CreateContext("GET", "/v1/models");

        await middleware.InvokeAsync(context);

        recordingMock.Verify(
            r => r.Record(It.Is<ProxyRequest>(req =>
                req.ResponseBody != null &&
                req.ResponseBody.Contains("[TRUNCATED:") &&
                req.ResponseBody.StartsWith(new string('X', 50)))),
            Times.Once);
    }

    [Test]
    public async Task RecordingService_RequestBodyExceedsLimit_ReceivesTruncatedBody()
    {
        // A request body larger than the configured limit should be truncated
        // before being handed to the recording service.
        var longBody = new string('Y', 200);

        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, $"{UpstreamBaseUrl}/v1/messages")
            .Respond(HttpStatusCode.OK, "application/json", """{"id":"msg_1"}""");

        var recordingMock = new Mock<IRecordingService>();
        var options = new UpstreamOptions { BaseUrl = UpstreamBaseUrl, MaxStoredBodyBytes = 50 };
        var middleware = CreateMiddlewareCore(mockHttp.ToHttpClient(), recordingMock.Object, options);
        var context = CreateContext("POST", "/v1/messages", body: longBody,
            headers: new Dictionary<string, string> { ["Content-Type"] = "application/json" });

        await middleware.InvokeAsync(context);

        recordingMock.Verify(
            r => r.Record(It.Is<ProxyRequest>(req =>
                req.RequestBody != null &&
                req.RequestBody.Contains("[TRUNCATED:") &&
                req.RequestBody.StartsWith(new string('Y', 50)))),
            Times.Once);
    }

    [Test]
    public async Task RecordingService_BodyWithinLimit_IsNotTruncated()
    {
        const string responseBody = """{"id":"msg_1"}""";

        var mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{UpstreamBaseUrl}/v1/models")
            .Respond(HttpStatusCode.OK, "application/json", responseBody);

        var recordingMock = new Mock<IRecordingService>();
        var options = new UpstreamOptions { BaseUrl = UpstreamBaseUrl, MaxStoredBodyBytes = 1_048_576 };
        var middleware = CreateMiddlewareCore(mockHttp.ToHttpClient(), recordingMock.Object, options);
        var context = CreateContext("GET", "/v1/models");

        await middleware.InvokeAsync(context);

        recordingMock.Verify(
            r => r.Record(It.Is<ProxyRequest>(req => req.ResponseBody == responseBody)),
            Times.Once);
    }

    // ── Test double ───────────────────────────────────────────────────────────

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public TestHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }
}
