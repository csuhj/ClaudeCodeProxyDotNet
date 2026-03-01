using ClaudeCodeProxy.Data;
using ClaudeCodeProxy.Models;

namespace ClaudeCodeProxy.Services;

/// <summary>
/// Queries aggregated request and token-usage statistics from the database
/// via <see cref="IRecordingRepository"/>.
/// </summary>
public class StatsService : IStatsService
{
    private readonly IRecordingRepository _repository;

    public StatsService(IRecordingRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public async Task<List<StatsBucket>> GetRequestsPerHourAsync(DateTime from, DateTime to)
    {
        var data = await _repository.GetStatsProjectionsAsync(from, to);

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
        var data = await _repository.GetStatsProjectionsAsync(from, to);

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
}
