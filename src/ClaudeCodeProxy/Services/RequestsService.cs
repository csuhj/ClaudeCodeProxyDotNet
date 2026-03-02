using ClaudeCodeProxy.Data;
using ClaudeCodeProxy.Models;

namespace ClaudeCodeProxy.Services;

/// <summary>
/// Retrieves individual LLM request records from the database via
/// <see cref="IRecordingRepository"/>.
/// </summary>
public class RequestsService : IRequestsService
{
    private const int MaxPageSize = 200;

    private readonly IRecordingRepository _repository;

    public RequestsService(IRecordingRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public async Task<List<LlmRequestSummary>> GetRecentLlmRequestsAsync(
        DateTime from,
        DateTime to,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var clampedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var skip = page * clampedPageSize;

        return await _repository.GetLlmRequestsAsync(from, to, skip, clampedPageSize, ct);
    }

    /// <inheritdoc/>
    public async Task<LlmRequestDetail?> GetLlmRequestDetailAsync(long id, CancellationToken ct = default)
    {
        return await _repository.GetLlmRequestByIdAsync(id, ct);
    }
}
