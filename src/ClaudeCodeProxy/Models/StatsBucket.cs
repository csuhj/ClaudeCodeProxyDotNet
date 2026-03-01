namespace ClaudeCodeProxy.Models;

/// <summary>
/// Aggregated statistics for a single time bucket (hour or day).
/// </summary>
public class StatsBucket
{
    /// <summary>The start of the time bucket (UTC, truncated to the hour or day).</summary>
    public DateTime TimeBucket { get; set; }

    /// <summary>Total number of proxied requests in this bucket.</summary>
    public int RequestCount { get; set; }

    /// <summary>Number of requests that were Anthropic LLM calls (have a linked LlmUsage row).</summary>
    public int LlmRequestCount { get; set; }

    /// <summary>Sum of input tokens across all LLM calls in this bucket.</summary>
    public long TotalInputTokens { get; set; }

    /// <summary>Sum of output tokens across all LLM calls in this bucket.</summary>
    public long TotalOutputTokens { get; set; }
}
