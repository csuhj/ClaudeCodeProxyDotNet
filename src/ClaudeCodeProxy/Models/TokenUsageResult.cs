namespace ClaudeCodeProxy.Models;

/// <summary>
/// Token usage extracted from an Anthropic Messages API response,
/// returned by <see cref="Services.TokenUsageParser"/>.
/// </summary>
public class TokenUsageResult
{
    public string? Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public int CacheCreationTokens { get; set; }
}
