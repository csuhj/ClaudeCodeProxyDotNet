using ClaudeCodeProxy.Data;
using ClaudeCodeProxy.Models;

namespace ClaudeCodeProxy.Services;

/// <summary>
/// Persists proxied request/response pairs to the SQLite database.
/// Recording is performed on a background thread so it never blocks the
/// response path back to the client.
/// </summary>
public class RecordingService : IRecordingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecordingService> _logger;

    public RecordingService(IServiceScopeFactory scopeFactory, ILogger<RecordingService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Enqueues a <see cref="ProxyRequest"/> record for background persistence.
    /// Returns immediately; database write failures are logged but never rethrown.
    /// </summary>
    public void Record(ProxyRequest request)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // DbContext is scoped — create a new scope for each background write.
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
                db.ProxyRequests.Add(request);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to record proxy request {Method} {Path} — recording will be skipped.",
                    request.Method, request.Path);
            }
        });
    }
}
