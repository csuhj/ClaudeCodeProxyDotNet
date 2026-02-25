namespace ClaudeCodeProxy.Models;

/// <summary>
/// Token usage extracted from a single Anthropic Messages API call.
/// Linked one-to-one with the parent <see cref="ProxyRequest"/>.
/// </summary>
public class LlmUsage
{
    public long Id { get; set; }

    /// <summary>Foreign key to the parent ProxyRequest.</summary>
    public long ProxyRequestId { get; set; }

    /// <summary>UTC time of the call (same as parent ProxyRequest.Timestamp).</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Model name from the response (e.g. "claude-opus-4-6").</summary>
    public string? Model { get; set; }

    /// <summary>Input tokens from usage.input_tokens.</summary>
    public int InputTokens { get; set; }

    /// <summary>Output tokens from usage.output_tokens.</summary>
    public int OutputTokens { get; set; }

    /// <summary>Cache read tokens from usage.cache_read_input_tokens (0 if absent).</summary>
    public int CacheReadTokens { get; set; }

    /// <summary>Cache creation tokens from usage.cache_creation_input_tokens (0 if absent).</summary>
    public int CacheCreationTokens { get; set; }

    // Navigation property
    public ProxyRequest ProxyRequest { get; set; } = null!;
}
