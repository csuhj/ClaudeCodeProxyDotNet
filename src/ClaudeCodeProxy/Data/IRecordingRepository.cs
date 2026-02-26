using ClaudeCodeProxy.Models;

namespace ClaudeCodeProxy.Data;

/// <summary>
/// Abstracts the persistence of <see cref="ProxyRequest"/> records,
/// decoupling the recording service from the EF Core DbContext.
/// </summary>
public interface IRecordingRepository
{
    Task AddAsync(ProxyRequest request, CancellationToken ct = default);
}
