using System.Text.Json;
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

    public async Task<List<LlmRequestSummary>> GetLlmRequestsAsync(
        DateTime from, DateTime to, int skip, int take, CancellationToken ct = default)
    {
        return await _db.ProxyRequests
            .Where(r => r.Timestamp >= from && r.Timestamp < to && r.LlmUsage != null)
            .OrderByDescending(r => r.Timestamp)
            .Skip(skip)
            .Take(take)
            .Select(r => new LlmRequestSummary
            {
                Id = r.Id,
                Timestamp = r.Timestamp,
                Method = r.Method,
                Path = r.Path,
                ResponseStatusCode = r.ResponseStatusCode,
                DurationMs = r.DurationMs,
                Model = r.LlmUsage!.Model,
                InputTokens = r.LlmUsage.InputTokens,
                OutputTokens = r.LlmUsage.OutputTokens,
                CacheReadTokens = r.LlmUsage.CacheReadTokens,
                CacheCreationTokens = r.LlmUsage.CacheCreationTokens,
            })
            .ToListAsync(ct);
    }

    public async Task<LlmRequestDetail?> GetLlmRequestByIdAsync(long id, CancellationToken ct = default)
    {
        var r = await _db.ProxyRequests
            .Include(r => r.LlmUsage)
            .SingleOrDefaultAsync(r => r.Id == id, ct);

        if (r == null) return null;

        return new LlmRequestDetail
        {
            Id = r.Id,
            Timestamp = r.Timestamp,
            Method = r.Method,
            Path = r.Path,
            ResponseStatusCode = r.ResponseStatusCode,
            DurationMs = r.DurationMs,
            Model = r.LlmUsage?.Model,
            InputTokens = r.LlmUsage?.InputTokens,
            OutputTokens = r.LlmUsage?.OutputTokens,
            CacheReadTokens = r.LlmUsage?.CacheReadTokens,
            CacheCreationTokens = r.LlmUsage?.CacheCreationTokens,
            RequestHeaders = r.RequestHeaders,
            RequestBody = r.RequestBody,
            ResponseHeaders = r.ResponseHeaders,
            ResponseBody = r.ResponseBody,
            IsStreaming = IsStreamingResponse(r.ResponseHeaders),
        };
    }

    /// <summary>
    /// Derives the streaming flag from the stored ResponseHeaders JSON by checking
    /// whether the Content-Type value contains "text/event-stream".
    /// </summary>
    private static bool IsStreamingResponse(string responseHeadersJson)
    {
        try
        {
            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(responseHeadersJson);
            if (headers == null) return false;

            foreach (var (key, value) in headers)
            {
                if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) &&
                    value.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
