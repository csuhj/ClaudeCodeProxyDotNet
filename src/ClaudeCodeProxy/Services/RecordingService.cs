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
    public void Record(ProxyRequest request) => _ = Task.Run(() => RecordCoreAsync(request));

    /// <summary>
    /// Performs the actual persistence. Exposed as <c>internal</c> so tests can
    /// await it directly without racing against a background <see cref="Task.Run"/>.
    /// </summary>
    internal async Task RecordCoreAsync(ProxyRequest request)
    {
        try
        {
            // Repository is scoped — create a new scope for each background write.
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRecordingRepository>();
            await repository.AddAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to record proxy request {Method} {Path} — recording will be skipped.",
                request.Method, request.Path);
        }
    }
}
