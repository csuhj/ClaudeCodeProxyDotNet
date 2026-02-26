using ClaudeCodeProxy.Models;

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
}
