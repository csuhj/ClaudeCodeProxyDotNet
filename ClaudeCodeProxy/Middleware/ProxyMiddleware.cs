using System.Diagnostics;
using ClaudeCodeProxy.Models;

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

    public ProxyMiddleware(
        RequestDelegate _,
        IHttpClientFactory httpClientFactory,
        ILogger<ProxyMiddleware> logger,
        UpstreamOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _upstreamBaseUrl = options.BaseUrl.TrimEnd('/');
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var requestAborted = context.RequestAborted;

        // ── Step 1: Buffer the request body ──────────────────────────────────
        // Read the entire request body into a MemoryStream so it can be both
        // forwarded to the upstream and later handed to the recording service.
        using var requestBodyMs = new MemoryStream();
        await context.Request.Body.CopyToAsync(requestBodyMs, requestAborted);
        requestBodyMs.Position = 0;

        // ── Step 2: Build the upstream request ───────────────────────────────
        var path = context.Request.Path.ToUriComponent();
        var query = context.Request.QueryString.ToUriComponent();
        var upstreamUri = new Uri(_upstreamBaseUrl + path + query);

        using var upstreamRequest = new HttpRequestMessage(
            new HttpMethod(context.Request.Method),
            upstreamUri);

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

        // ── Step 3: Send to upstream ─────────────────────────────────────────
        // ResponseHeadersRead lets us begin streaming the body immediately
        // rather than waiting for the full response to be buffered by HttpClient.
        HttpResponseMessage upstreamResponse;
        try
        {
            var client = _httpClientFactory.CreateClient("upstream");
            upstreamResponse = await client.SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseHeadersRead,
                requestAborted);
        }
        catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
        {
            return; // Client cancelled — nothing to do
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Upstream timed out: {Method} {Path}",
                context.Request.Method, context.Request.Path.Value);
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            await context.Response.WriteAsync("Gateway Timeout: upstream did not respond in time.", requestAborted);
            return;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Upstream connection failed: {Method} {Path}",
                context.Request.Method, context.Request.Path.Value);
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync("Bad Gateway: could not connect to upstream.", requestAborted);
            return;
        }

        using (upstreamResponse)
        {
            // ── Step 4: Copy response status and headers ──────────────────────
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

            // ── Step 5: Stream or buffer the response body ────────────────────
            var isStreaming = upstreamResponse.Content.Headers.ContentType?.MediaType
                ?.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase) ?? false;

            using var responseBodyMs = new MemoryStream();

            try
            {
                await using var upstreamStream =
                    await upstreamResponse.Content.ReadAsStreamAsync(requestAborted);

                if (isStreaming)
                {
                    // Forward each chunk to the client immediately while
                    // accumulating a copy for the recording service.
                    var buffer = new byte[4096];
                    int bytesRead;
                    while ((bytesRead = await upstreamStream.ReadAsync(buffer, requestAborted)) > 0)
                    {
                        await context.Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead), requestAborted);
                        await context.Response.Body.FlushAsync(requestAborted);
                        responseBodyMs.Write(buffer, 0, bytesRead);
                    }
                }
                else
                {
                    // Buffer the full response, then write it all at once.
                    await upstreamStream.CopyToAsync(responseBodyMs, requestAborted);
                    responseBodyMs.Position = 0;
                    await responseBodyMs.CopyToAsync(context.Response.Body, requestAborted);
                }
            }
            catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
            {
                // Client disconnected mid-stream — expected, not an error.
                return;
            }

            sw.Stop();

            // ── Step 6: TODO (Phase 3) — hand off to RecordingService ─────────
            // At this point the following are available for persistence:
            //   requestBodyMs  — buffered request body
            //   responseBodyMs — buffered response body (full or accumulated SSE)
            //   upstreamResponse.StatusCode
            //   sw.ElapsedMilliseconds
            //   isStreaming

            _logger.LogInformation(
                "{Method} {Path} -> {StatusCode} ({Ms}ms, {Mode})",
                context.Request.Method,
                context.Request.Path.Value,
                (int)upstreamResponse.StatusCode,
                sw.ElapsedMilliseconds,
                isStreaming ? "streaming" : "buffered");
        }
    }
}
