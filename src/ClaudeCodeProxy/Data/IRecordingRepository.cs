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
}
