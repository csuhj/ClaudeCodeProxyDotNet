using ClaudeCodeProxy.Data;
using ClaudeCodeProxy.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClaudeCodeProxy.Tests.Data;

/// <summary>
/// Tests for <see cref="RecordingRepository"/> using a real in-memory SQLite database.
/// </summary>
[TestFixture]
public class RecordingRepositoryTests
{
    private SqliteConnection _connection = null!;
    private ProxyDbContext _db = null!;
    private RecordingRepository _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ProxyDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ProxyDbContext(options);
        _db.Database.EnsureCreated();

        _sut = new RecordingRepository(_db);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Test]
    public async Task AddAsync_SavesProxyRequestToDatabase()
    {
        var request = new ProxyRequest
        {
            Timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Method = "POST",
            Path = "/v1/messages",
            RequestHeaders = "{}",
            ResponseHeaders = "{}",
            ResponseStatusCode = 200,
            DurationMs = 55
        };

        await _sut.AddAsync(request);

        var saved = await _db.ProxyRequests.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(saved.Id, Is.GreaterThan(0));
            Assert.That(saved.Method, Is.EqualTo("POST"));
            Assert.That(saved.Path, Is.EqualTo("/v1/messages"));
            Assert.That(saved.ResponseStatusCode, Is.EqualTo(200));
            Assert.That(saved.DurationMs, Is.EqualTo(55));
            Assert.That(saved.Timestamp, Is.EqualTo(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)));
        });
    }

    [Test]
    public async Task AddAsync_WithLlmUsage_SavesBothRowsAndLinksCorrectly()
    {
        var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var request = new ProxyRequest
        {
            Timestamp = timestamp,
            Method = "POST",
            Path = "/v1/messages",
            RequestHeaders = "{}",
            ResponseHeaders = "{}",
            ResponseStatusCode = 200,
            DurationMs = 30,
            LlmUsage = new LlmUsage
            {
                Timestamp = timestamp,
                Model = "claude-sonnet-4-6",
                InputTokens = 10,
                OutputTokens = 25,
                CacheReadTokens = 100,
                CacheCreationTokens = 50
            }
        };

        await _sut.AddAsync(request);

        var savedRequest = await _db.ProxyRequests.SingleAsync();
        var savedUsage = await _db.LlmUsages.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(savedUsage.ProxyRequestId, Is.EqualTo(savedRequest.Id));
            Assert.That(savedUsage.Model, Is.EqualTo("claude-sonnet-4-6"));
            Assert.That(savedUsage.InputTokens, Is.EqualTo(10));
            Assert.That(savedUsage.OutputTokens, Is.EqualTo(25));
            Assert.That(savedUsage.CacheReadTokens, Is.EqualTo(100));
            Assert.That(savedUsage.CacheCreationTokens, Is.EqualTo(50));
        });
    }

    [Test]
    public async Task AddAsync_WithoutLlmUsage_SavesOnlyProxyRequest()
    {
        var request = new ProxyRequest
        {
            Timestamp = DateTime.UtcNow,
            Method = "GET",
            Path = "/health",
            RequestHeaders = "{}",
            ResponseHeaders = "{}",
            ResponseStatusCode = 200,
            DurationMs = 2
        };

        await _sut.AddAsync(request);

        Assert.That(await _db.ProxyRequests.CountAsync(), Is.EqualTo(1));
        Assert.That(await _db.LlmUsages.AnyAsync(), Is.False);
    }
}
