namespace ClaudeCodeProxy.Models;

/// <summary>
/// Full detail DTO for a single LLM request, used in drill-down views.
/// Extends <see cref="LlmRequestSummary"/> with the full request/response bodies and headers.
/// </summary>
public class LlmRequestDetail : LlmRequestSummary
{
    /// <summary>JSON-serialised dictionary of relevant request headers.</summary>
    public string RequestHeaders { get; set; } = string.Empty;

    /// <summary>Raw request body text; null when the request had no body.</summary>
    public string? RequestBody { get; set; }

    /// <summary>JSON-serialised dictionary of response headers.</summary>
    public string ResponseHeaders { get; set; } = string.Empty;

    /// <summary>Raw response body text; null when the response had no body.</summary>
    public string? ResponseBody { get; set; }

    /// <summary>
    /// True when the response was a Server-Sent Events stream (Content-Type: text/event-stream).
    /// Signals to the frontend which rendering path to use for the response body.
    /// </summary>
    public bool IsStreaming { get; set; }
}
