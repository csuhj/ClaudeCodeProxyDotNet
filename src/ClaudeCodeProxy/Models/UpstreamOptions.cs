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

    /// <summary>
    /// Maximum number of bytes stored for each request or response body in the
    /// database.  Bodies that exceed this limit are truncated and a note is
    /// appended so the truncation is visible in recorded data.
    /// Default is 1 048 576 (1 MiB).
    /// </summary>
    public int MaxStoredBodyBytes { get; set; } = 1_048_576;
}
