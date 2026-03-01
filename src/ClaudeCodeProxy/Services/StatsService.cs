using ClaudeCodeProxy.Data;
using ClaudeCodeProxy.Models;
using Microsoft.EntityFrameworkCore;

namespace ClaudeCodeProxy.Services;

/// <summary>
/// Queries aggregated request and token-usage statistics from the database.
/// </summary>
public class StatsService : IStatsService
{
    private readonly ProxyDbContext _db;

    public StatsService(ProxyDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<List<StatsBucket>> GetRequestsPerHourAsync(DateTime from, DateTime to)
    {
        var data = await FetchProjectedDataAsync(from, to);

        return data
            .GroupBy(r => new DateTime(r.Timestamp.Year, r.Timestamp.Month, r.Timestamp.Day,
                                       r.Timestamp.Hour, 0, 0, DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .Select(g => new StatsBucket
            {
                TimeBucket = g.Key,
                RequestCount = g.Count(),
                LlmRequestCount = g.Count(r => r.HasLlm),
                TotalInputTokens = g.Sum(r => (long)r.InputTokens),
                TotalOutputTokens = g.Sum(r => (long)r.OutputTokens),
            })
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<List<StatsBucket>> GetRequestsPerDayAsync(DateTime from, DateTime to)
    {
        var data = await FetchProjectedDataAsync(from, to);

        return data
            .GroupBy(r => new DateTime(r.Timestamp.Year, r.Timestamp.Month, r.Timestamp.Day,
                                       0, 0, 0, DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .Select(g => new StatsBucket
            {
                TimeBucket = g.Key,
                RequestCount = g.Count(),
                LlmRequestCount = g.Count(r => r.HasLlm),
                TotalInputTokens = g.Sum(r => (long)r.InputTokens),
                TotalOutputTokens = g.Sum(r => (long)r.OutputTokens),
            })
            .ToList();
    }

    /// <summary>
    /// Fetches all requests in [from, to) projected to the minimal fields needed for
    /// bucketing, performing a LEFT JOIN with LlmUsages via the navigation property.
    /// Grouping is done in memory so that date-truncation logic is not database-dialect-specific.
    /// </summary>
    private async Task<List<RequestProjection>> FetchProjectedDataAsync(DateTime from, DateTime to)
    {
        return await _db.ProxyRequests
            .Where(r => r.Timestamp >= from && r.Timestamp < to)
            .Select(r => new RequestProjection
            {
                Timestamp = r.Timestamp,
                HasLlm = r.LlmUsage != null,
                InputTokens = r.LlmUsage != null ? r.LlmUsage.InputTokens : 0,
                OutputTokens = r.LlmUsage != null ? r.LlmUsage.OutputTokens : 0,
            })
            .ToListAsync();
    }

    private sealed class RequestProjection
    {
        public DateTime Timestamp { get; init; }
        public bool HasLlm { get; init; }
        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }
    }
}
