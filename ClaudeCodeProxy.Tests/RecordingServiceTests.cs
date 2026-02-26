using ClaudeCodeProxy.Data;
using ClaudeCodeProxy.Models;
using ClaudeCodeProxy.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClaudeCodeProxy.Tests;

/// <summary>
/// Tests for <see cref="RecordingService"/> using a real in-memory SQLite database.
/// A single <see cref="SqliteConnection"/> is kept open for the lifetime of each
/// test so that all service scopes share the same underlying in-memory database.
/// </summary>
[TestFixture]
public class RecordingServiceTests
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;
    private RecordingService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        // Keep the connection open so the in-memory database persists across scopes.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<ProxyDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<IRecordingRepository, RecordingRepository>();
        _serviceProvider = services.BuildServiceProvider();

        // Create the schema once using the shared connection.
        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ProxyDbContext>().Database.EnsureCreated();

        _sut = new RecordingService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RecordingService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    [Test]
    public async Task RecordCoreAsync_PersistsProxyRequestToDatabase()
    {
        var request = new ProxyRequest
        {
            Timestamp = DateTime.UtcNow,
            Method = "POST",
            Path = "/v1/messages",
            RequestHeaders = "{}",
            ResponseHeaders = "{}",
            ResponseStatusCode = 200,
            DurationMs = 42
        };

        await _sut.RecordCoreAsync(request);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        var saved = await db.ProxyRequests.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(saved.Method, Is.EqualTo("POST"));
            Assert.That(saved.Path, Is.EqualTo("/v1/messages"));
            Assert.That(saved.ResponseStatusCode, Is.EqualTo(200));
            Assert.That(saved.DurationMs, Is.EqualTo(42));
        });
    }
}
