using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ClaudeCodeProxy.Models;
using ClaudeCodeProxy.Services;

namespace ClaudeCodeProxy.Middleware;

public class ProxyMiddleware
{
    // Standard hop-by-hop headers that must not be forwarded between hops.
    // Content-Length is also excluded from responses so Kestrel can set it
    // correctly after any buffering (avoids mismatches with the upstream value).
    private static readonly HashSet<string> HopByHopRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailers", "Transfer-Encoding", "Upgrade", "Proxy-Connection",
        // Host is set by HttpClient to match the upstream; Content-Length is
        // recalculated from the buffered body.
        "Host", "Content-Length"
    };

    private static readonly HashSet<string> HopByHopResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailers", "Transfer-Encoding", "Upgrade", "Proxy-Connection",
        // Strip Content-Length so Kestrel sets it based on what we actually write.
        "Content-Length"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProxyMiddleware> _logger;
    private readonly string _upstreamBaseUrl;
    private readonly IRecordingService _recordingService;

    public ProxyMiddleware(
        RequestDelegate _,
        IHttpClientFactory httpClientFactory,
        ILogger<ProxyMiddleware> logger,
        UpstreamOptions options,
        IRecordingService recordingService)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _upstreamBaseUrl = options.BaseUrl.TrimEnd('/');
        _recordingService = recordingService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var requestAborted = context.RequestAborted;
        var timestamp = DateTime.UtcNow;
        var pathAndQuery = context.Request.Path.ToUriComponent() + context.Request.QueryString.ToUriComponent();

        // ── Step 1: Buffer the request body ──────────────────────────────────
        using var requestBodyMs = new MemoryStream();
        var requestHeadersJson = await BufferRequestBodyAsync(context, requestBodyMs, requestAborted);

        // ── Step 2: Build the upstream request ───────────────────────────────
        using var upstreamRequest = BuildUpstreamRequest(context, requestBodyMs, pathAndQuery);

        // ── Step 3: Send to upstream ─────────────────────────────────────────
        var upstreamResponse = await SendToUpstreamAsync(upstreamRequest, context, requestAborted);
        if (upstreamResponse == null) return;

        using (upstreamResponse)
        {
            // ── Step 4: Copy response status and headers ──────────────────────
            var responseHeadersJson = CopyResponseHeaders(context, upstreamResponse);

            // ── Step 5: Stream or buffer the response body ────────────────────
            using var responseBodyMs = new MemoryStream();
            var isStreaming = upstreamResponse.Content.Headers.ContentType?.MediaType
                ?.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase) ?? false;

            if (!await WriteResponseBodyAsync(context, upstreamResponse, responseBodyMs, isStreaming, requestAborted))
                return; // Client disconnected mid-stream

            sw.Stop();
            _logger.LogInformation(
                "{Method} {Path} -> {StatusCode} ({Ms}ms, {Mode})",
                context.Request.Method,
                context.Request.Path.Value,
                (int)upstreamResponse.StatusCode,
                sw.ElapsedMilliseconds,
                isStreaming ? "streaming" : "buffered");

            // ── Step 6: Hand off to RecordingService ──────────────────────────
            await RecordRequestAsync(
                context, upstreamResponse, requestBodyMs, responseBodyMs,
                requestHeadersJson, responseHeadersJson, timestamp, sw.ElapsedMilliseconds, pathAndQuery);
        }
    }

    // ── Step 1 ────────────────────────────────────────────────────────────────
    // Read the entire request body into a MemoryStream so it can be both
    // forwarded to the upstream and later handed to the recording service.
    // Returns the request headers serialised as JSON for recording.
    private static async Task<string> BufferRequestBodyAsync(
        HttpContext context, MemoryStream requestBodyMs, CancellationToken ct)
    {
        await context.Request.Body.CopyToAsync(requestBodyMs, ct);
        requestBodyMs.Position = 0;

        return SerializeHeaders(
            context.Request.Headers.Select(h => KeyValuePair.Create(h.Key, h.Value.ToString())));
    }

    // ── Step 2 ────────────────────────────────────────────────────────────────
    // Build the HttpRequestMessage to send to the upstream, copying the method,
    // URI, body, and all non-hop-by-hop headers from the incoming request.
    private HttpRequestMessage BuildUpstreamRequest(
        HttpContext context, MemoryStream requestBodyMs, string pathAndQuery)
    {
        var upstreamUri = new Uri(_upstreamBaseUrl + pathAndQuery);
        var upstreamRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), upstreamUri);

        // Attach body content (required before forwarding content headers).
        if (requestBodyMs.Length > 0)
            upstreamRequest.Content = new StreamContent(requestBodyMs);

        // Copy request headers, excluding hop-by-hop and hosting headers.
        // TryAddWithoutValidation returns false for content headers (e.g.
        // Content-Type), so those are forwarded to the Content instead.
        foreach (var (key, value) in context.Request.Headers)
        {
            if (HopByHopRequestHeaders.Contains(key)) continue;

            if (!upstreamRequest.Headers.TryAddWithoutValidation(key, value.ToArray()))
                upstreamRequest.Content?.Headers.TryAddWithoutValidation(key, value.ToArray());
        }

        return upstreamRequest;
    }

    // ── Step 3 ────────────────────────────────────────────────────────────────
    // Send the request to the upstream. ResponseHeadersRead lets us begin
    // streaming the body immediately rather than waiting for the full response
    // to be buffered by HttpClient.
    // Returns null if the request was cancelled or the upstream was unreachable
    // (the error response is written to context.Response before returning null).
    private async Task<HttpResponseMessage?> SendToUpstreamAsync(
        HttpRequestMessage upstreamRequest, HttpContext context, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("upstream");
            return await client.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null; // Client cancelled — nothing to do
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Upstream timed out: {Method} {Path}",
                context.Request.Method, context.Request.Path.Value);
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            await context.Response.WriteAsync("Gateway Timeout: upstream did not respond in time.", ct);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Upstream connection failed: {Method} {Path}",
                context.Request.Method, context.Request.Path.Value);
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync("Bad Gateway: could not connect to upstream.", ct);
            return null;
        }
    }

    // ── Step 4 ────────────────────────────────────────────────────────────────
    // Copy the upstream status code and all non-hop-by-hop response headers
    // (including content headers) to the client response.
    // Returns the response headers serialised as JSON for recording.
    private static string CopyResponseHeaders(HttpContext context, HttpResponseMessage upstreamResponse)
    {
        context.Response.StatusCode = (int)upstreamResponse.StatusCode;

        foreach (var (key, value) in upstreamResponse.Headers)
        {
            if (HopByHopResponseHeaders.Contains(key)) continue;
            context.Response.Headers[key] = value.ToArray();
        }

        // Content headers (Content-Type, Content-Encoding, etc.) live on
        // the Content object rather than the main response headers.
        foreach (var (key, value) in upstreamResponse.Content.Headers)
        {
            if (HopByHopResponseHeaders.Contains(key)) continue;
            context.Response.Headers[key] = value.ToArray();
        }

        return SerializeHeaders(
            upstreamResponse.Headers.Concat(upstreamResponse.Content.Headers)
                .Select(h => KeyValuePair.Create(h.Key, string.Join(", ", h.Value))));
    }

    // ── Step 5 ────────────────────────────────────────────────────────────────
    // Stream or buffer the upstream response body to the client while also
    // accumulating a copy in responseBodyMs for the recording service.
    // Returns false if the client disconnected mid-stream (expected, not an error).
    private static async Task<bool> WriteResponseBodyAsync(
        HttpContext context, HttpResponseMessage upstreamResponse,
        MemoryStream responseBodyMs, bool isStreaming, CancellationToken ct)
    {
        try
        {
            await using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(ct);

            if (isStreaming)
            {
                // Forward each chunk to the client immediately while
                // accumulating a copy for the recording service.
                var buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = await upstreamStream.ReadAsync(buffer, ct)) > 0)
                {
                    await context.Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    await context.Response.Body.FlushAsync(ct);
                    responseBodyMs.Write(buffer, 0, bytesRead);
                }
            }
            else
            {
                // Buffer the full response, then write it all at once.
                await upstreamStream.CopyToAsync(responseBodyMs, ct);
                responseBodyMs.Position = 0;
                await responseBodyMs.CopyToAsync(context.Response.Body, ct);
            }

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
    }

    // ── Step 6 ────────────────────────────────────────────────────────────────
    // Build the ProxyRequest record from all captured data and fire-and-forget
    // the save via the recording service.
    private async Task RecordRequestAsync(
        HttpContext context, HttpResponseMessage upstreamResponse,
        MemoryStream requestBodyMs, MemoryStream responseBodyMs,
        string requestHeadersJson, string responseHeadersJson,
        DateTime timestamp, long durationMs, string pathAndQuery)
    {
        requestBodyMs.Position = 0;
        var requestBodyText = requestBodyMs.Length > 0
            ? await ReadAsStringAsync(requestBodyMs)
            : null;

        responseBodyMs.Position = 0;
        var responseBodyText = responseBodyMs.Length > 0
            ? await ReadAsStringAsync(responseBodyMs)
            : null;

        var record = new ProxyRequest
        {
            Timestamp = timestamp,
            Method = context.Request.Method,
            Path = pathAndQuery,
            RequestHeaders = requestHeadersJson,
            RequestBody = requestBodyText,
            ResponseStatusCode = (int)upstreamResponse.StatusCode,
            ResponseHeaders = responseHeadersJson,
            ResponseBody = responseBodyText,
            DurationMs = durationMs
        };

        _recordingService.Record(record);
    }

    private static string SerializeHeaders(IEnumerable<KeyValuePair<string, string>> headers)
    {
        var dict = headers.ToDictionary(h => h.Key, h => h.Value);
        return JsonSerializer.Serialize(dict);
    }

    private static async Task<string> ReadAsStringAsync(MemoryStream ms)
    {
        using var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
