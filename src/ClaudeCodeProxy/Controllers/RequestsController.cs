using ClaudeCodeProxy.Models;
using ClaudeCodeProxy.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeCodeProxy.Controllers;

[ApiController]
[Route("api/requests")]
public class RequestsController : ControllerBase
{
    private readonly IRequestsService _requestsService;

    public RequestsController(IRequestsService requestsService)
    {
        _requestsService = requestsService;
    }

    /// <summary>
    /// Returns a paginated list of LLM requests, ordered newest-first.
    /// Defaults to the last 24 hours when <paramref name="from"/> and
    /// <paramref name="to"/> are omitted.
    /// </summary>
    /// <param name="from">Start of the query window (UTC). Defaults to 24 hours ago.</param>
    /// <param name="to">End of the query window (UTC, exclusive). Defaults to now.</param>
    /// <param name="page">Zero-based page index. Defaults to 0.</param>
    /// <param name="pageSize">Results per page (max 200). Defaults to 50.</param>
    [HttpGet]
    public async Task<ActionResult<List<LlmRequestSummary>>> GetRequests(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var toDate = (to ?? DateTime.UtcNow).ToUniversalTime();
        var fromDate = (from ?? toDate.AddDays(-1)).ToUniversalTime();

        var result = await _requestsService.GetRecentLlmRequestsAsync(fromDate, toDate, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>
    /// Returns the full detail for a single LLM request by its database id.
    /// </summary>
    /// <param name="id">The ProxyRequest id.</param>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<LlmRequestDetail>> GetRequestById(long id, CancellationToken ct)
    {
        var result = await _requestsService.GetLlmRequestDetailAsync(id, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }
}
