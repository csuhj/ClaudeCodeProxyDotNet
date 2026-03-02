using ClaudeCodeProxy.Models;

namespace ClaudeCodeProxy.Data;

/// <summary>
/// Abstracts the persistence of and queries against <see cref="ProxyRequest"/> records,
/// decoupling services from the EF Core DbContext.
/// </summary>
public interface IRecordingRepository
{
    Task AddAsync(ProxyRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns a lightweight projection of every request whose timestamp falls in
    /// [<paramref name="from"/>, <paramref name="to"/>), including LLM token counts
    /// where available, for use by the stats aggregation service.
    /// </summary>
    Task<List<StatsProjection>> GetStatsProjectionsAsync(
        DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>
    /// Returns a paginated list of LLM requests (those with an associated
    /// <see cref="LlmUsage"/> record) whose timestamp falls in
    /// [<paramref name="from"/>, <paramref name="to"/>), ordered newest-first.
    /// Body content is excluded; use <see cref="GetLlmRequestByIdAsync"/> for that.
    /// </summary>
    Task<List<LlmRequestSummary>> GetLlmRequestsAsync(
        DateTime from, DateTime to, int skip, int take, CancellationToken ct = default);

    /// <summary>
    /// Returns the full detail for a single request by <paramref name="id"/>,
    /// or <c>null</c> if no matching record exists.
    /// </summary>
    Task<LlmRequestDetail?> GetLlmRequestByIdAsync(long id, CancellationToken ct = default);
}
