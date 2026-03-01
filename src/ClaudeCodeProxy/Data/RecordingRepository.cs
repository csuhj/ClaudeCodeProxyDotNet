using ClaudeCodeProxy.Models;
using Microsoft.EntityFrameworkCore;

namespace ClaudeCodeProxy.Data;

/// <summary>
/// EF Core implementation of <see cref="IRecordingRepository"/>.
/// Depends on the scoped <see cref="ProxyDbContext"/> — register as scoped.
/// </summary>
public class RecordingRepository : IRecordingRepository
{
    private readonly ProxyDbContext _db;

    public RecordingRepository(ProxyDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(ProxyRequest request, CancellationToken ct = default)
    {
        _db.ProxyRequests.Add(request);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<StatsProjection>> GetStatsProjectionsAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await _db.ProxyRequests
            .Where(r => r.Timestamp >= from && r.Timestamp < to)
            .Select(r => new StatsProjection(
                r.Timestamp,
                r.LlmUsage != null,
                r.LlmUsage != null ? r.LlmUsage.InputTokens : 0,
                r.LlmUsage != null ? r.LlmUsage.OutputTokens : 0))
            .ToListAsync(ct);
    }
}
