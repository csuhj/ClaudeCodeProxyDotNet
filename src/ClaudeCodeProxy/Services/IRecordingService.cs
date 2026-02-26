using ClaudeCodeProxy.Models;

namespace ClaudeCodeProxy.Services;

/// <summary>
/// Persists a captured proxy request/response pair to the database.
/// Implementations should be non-blocking (fire-and-forget) so that
/// recording never delays the response returned to the client.
/// </summary>
public interface IRecordingService
{
    void Record(ProxyRequest request);
}
