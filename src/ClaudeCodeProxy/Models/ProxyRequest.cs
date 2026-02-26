namespace ClaudeCodeProxy.Models;

/// <summary>
/// Represents one recorded proxied HTTP request/response pair.
/// </summary>
public class ProxyRequest
{
    public long Id { get; set; }

    /// <summary>UTC time the request was received by the proxy.</summary>
    public DateTime Timestamp { get; set; }

    public string Method { get; set; } = string.Empty;

    /// <summary>Request path including query string.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>JSON-serialised dictionary of relevant request headers.</summary>
    public string RequestHeaders { get; set; } = string.Empty;

    /// <summary>Raw request body text; null when the request had no body.</summary>
    public string? RequestBody { get; set; }

    public int ResponseStatusCode { get; set; }

    /// <summary>JSON-serialised dictionary of response headers.</summary>
    public string ResponseHeaders { get; set; } = string.Empty;

    /// <summary>Raw response body text; null when the response had no body.</summary>
    public string? ResponseBody { get; set; }

    /// <summary>Total proxy duration in milliseconds (from request received to response fully sent).</summary>
    public long DurationMs { get; set; }

    // Navigation property
    public LlmUsage? LlmUsage { get; set; }
}
