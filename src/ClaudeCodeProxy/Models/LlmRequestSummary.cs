namespace ClaudeCodeProxy.Models;

/// <summary>
/// Lightweight DTO representing a single LLM request for use in list views.
/// Contains metadata only — no request/response body content.
/// </summary>
public class LlmRequestSummary
{
    public long Id { get; set; }

    /// <summary>UTC time the request was received by the proxy.</summary>
    public DateTime Timestamp { get; set; }

    public string Method { get; set; } = string.Empty;

    /// <summary>Request path including query string.</summary>
    public string Path { get; set; } = string.Empty;

    public int ResponseStatusCode { get; set; }

    /// <summary>Total proxy duration in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>Model name from the LLM response (e.g. "claude-sonnet-4-6").</summary>
    public string? Model { get; set; }

    /// <summary>Input tokens from the linked LlmUsage record.</summary>
    public int? InputTokens { get; set; }

    /// <summary>Output tokens from the linked LlmUsage record.</summary>
    public int? OutputTokens { get; set; }

    /// <summary>Cache read tokens from the linked LlmUsage record.</summary>
    public int? CacheReadTokens { get; set; }

    /// <summary>Cache creation tokens from the linked LlmUsage record.</summary>
    public int? CacheCreationTokens { get; set; }
}
