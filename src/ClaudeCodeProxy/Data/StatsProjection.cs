namespace ClaudeCodeProxy.Data;

/// <summary>
/// Lightweight projection of a single <see cref="ProxyRequest"/> row (with its optional
/// <see cref="ClaudeCodeProxy.Models.LlmUsage"/>) used by <see cref="IRecordingRepository.GetStatsProjectionsAsync"/>.
/// </summary>
public sealed record StatsProjection(
    DateTime Timestamp,
    bool HasLlm,
    int InputTokens,
    int OutputTokens);
