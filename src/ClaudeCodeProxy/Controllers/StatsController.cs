using ClaudeCodeProxy.Models;
using ClaudeCodeProxy.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudeCodeProxy.Controllers;

[ApiController]
[Route("api/stats")]
public class StatsController : ControllerBase
{
    private readonly IStatsService _statsService;

    public StatsController(IStatsService statsService)
    {
        _statsService = statsService;
    }

    /// <summary>Returns request and token-usage aggregates bucketed by hour.</summary>
    /// <param name="from">Start of the query window (UTC). Defaults to 7 days ago.</param>
    /// <param name="to">End of the query window (UTC, exclusive). Defaults to now.</param>
    [HttpGet("hourly")]
    public async Task<ActionResult<List<StatsBucket>>> GetHourly(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var toDate = (to ?? DateTime.UtcNow).ToUniversalTime();
        var fromDate = (from ?? toDate.AddDays(-7)).ToUniversalTime();

        var result = await _statsService.GetRequestsPerHourAsync(fromDate, toDate);
        return Ok(result);
    }

    /// <summary>Returns request and token-usage aggregates bucketed by day.</summary>
    /// <param name="from">Start of the query window (UTC). Defaults to 7 days ago.</param>
    /// <param name="to">End of the query window (UTC, exclusive). Defaults to now.</param>
    [HttpGet("daily")]
    public async Task<ActionResult<List<StatsBucket>>> GetDaily(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var toDate = (to ?? DateTime.UtcNow).ToUniversalTime();
        var fromDate = (from ?? toDate.AddDays(-7)).ToUniversalTime();

        var result = await _statsService.GetRequestsPerDayAsync(fromDate, toDate);
        return Ok(result);
    }
}
