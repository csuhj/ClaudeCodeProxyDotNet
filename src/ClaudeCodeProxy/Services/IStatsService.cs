using ClaudeCodeProxy.Models;

namespace ClaudeCodeProxy.Services;

public interface IStatsService
{
    Task<List<StatsBucket>> GetRequestsPerHourAsync(DateTime from, DateTime to);
    Task<List<StatsBucket>> GetRequestsPerDayAsync(DateTime from, DateTime to);
}
