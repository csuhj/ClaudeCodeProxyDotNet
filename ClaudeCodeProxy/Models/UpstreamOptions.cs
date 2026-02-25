namespace ClaudeCodeProxy.Models;

public class UpstreamOptions
{
    public const string SectionName = "Upstream";

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Maximum time in seconds to wait for the upstream to respond.
    /// Default is 300 (5 minutes) to accommodate long-running LLM responses.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;
}
