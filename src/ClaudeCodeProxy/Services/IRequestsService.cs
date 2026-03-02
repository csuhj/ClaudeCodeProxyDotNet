using ClaudeCodeProxy.Models;

namespace ClaudeCodeProxy.Services;

public interface IRequestsService
{
    /// <summary>
    /// Returns a paginated list of LLM requests whose timestamp falls in
    /// [<paramref name="from"/>, <paramref name="to"/>), ordered newest-first.
    /// </summary>
    Task<List<LlmRequestSummary>> GetRecentLlmRequestsAsync(
        DateTime from,
        DateTime to,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the full detail for a single request by <paramref name="id"/>,
    /// or <c>null</c> if no matching record exists.
    /// </summary>
    Task<LlmRequestDetail?> GetLlmRequestDetailAsync(long id, CancellationToken ct = default);
}
