using ClaudeCodeProxy.Data;
using ClaudeCodeProxy.Models;
using ClaudeCodeProxy.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClaudeCodeProxy.Tests.Services;

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

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private const string NonStreamingResponseBody = """
        {
          "type": "message",
          "id": "msg_01XFD",
          "model": "claude-sonnet-4-6",
          "role": "assistant",
          "content": [{"type": "text", "text": "Hi!"}],
          "stop_reason": "end_turn",
          "usage": {
            "input_tokens": 10,
            "output_tokens": 25,
            "cache_read_input_tokens": 100,
            "cache_creation_input_tokens": 50
          }
        }
        """;

    private const string StreamingResponseBody =
        "event: message_start\n" +
        """data: {"type":"message_start","message":{"model":"claude-sonnet-4-6","usage":{"input_tokens":3,"cache_creation_input_tokens":1886,"cache_read_input_tokens":18685,"output_tokens":0}}}""" + "\n\n" +
        "event: content_block_delta\n" +
        """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello!"}}""" + "\n\n" +
        "event: message_delta\n" +
        """data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"input_tokens":3,"cache_creation_input_tokens":1886,"cache_read_input_tokens":18685,"output_tokens":176}}""" + "\n\n" +
        "event: message_stop\n" +
        """data: {"type":"message_stop"}""" + "\n";

    // ── ProxyRequest persistence ───────────────────────────────────────────────

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

    // ── LlmUsage: non-streaming ────────────────────────────────────────────────

    [Test]
    public async Task RecordCoreAsync_NonStreamingLlmCall_SavesLlmUsageRecord()
    {
        var request = new ProxyRequest
        {
            Timestamp = DateTime.UtcNow,
            Method = "POST",
            Path = "/v1/messages",
            RequestHeaders = "{}",
            ResponseHeaders = """{"Content-Type": "application/json"}""",
            ResponseBody = NonStreamingResponseBody,
            ResponseStatusCode = 200,
            DurationMs = 10
        };

        await _sut.RecordCoreAsync(request);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        var usage = await db.LlmUsages.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(usage.Model, Is.EqualTo("claude-sonnet-4-6"));
            Assert.That(usage.InputTokens, Is.EqualTo(10));
            Assert.That(usage.OutputTokens, Is.EqualTo(25));
            Assert.That(usage.CacheReadTokens, Is.EqualTo(100));
            Assert.That(usage.CacheCreationTokens, Is.EqualTo(50));
        });
    }

    [Test]
    public async Task RecordCoreAsync_NonStreamingLlmCall_LlmUsageLinkedToProxyRequest()
    {
        var now = DateTime.UtcNow;
        var request = new ProxyRequest
        {
            Timestamp = now,
            Method = "POST",
            Path = "/v1/messages",
            RequestHeaders = "{}",
            ResponseHeaders = """{"Content-Type": "application/json"}""",
            ResponseBody = NonStreamingResponseBody,
            ResponseStatusCode = 200,
            DurationMs = 10
        };

        await _sut.RecordCoreAsync(request);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        var proxyRequest = await db.ProxyRequests.SingleAsync();
        var usage = await db.LlmUsages.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(usage.ProxyRequestId, Is.EqualTo(proxyRequest.Id));
            Assert.That(usage.Timestamp, Is.EqualTo(now));
        });
    }

    // ── LlmUsage: streaming ────────────────────────────────────────────────────

    [Test]
    public async Task RecordCoreAsync_StreamingLlmCall_SavesLlmUsageRecord()
    {
        var request = new ProxyRequest
        {
            Timestamp = DateTime.UtcNow,
            Method = "POST",
            Path = "/v1/messages",
            RequestHeaders = "{}",
            ResponseHeaders = """{"Content-Type": "text/event-stream"}""",
            ResponseBody = StreamingResponseBody,
            ResponseStatusCode = 200,
            DurationMs = 15
        };

        await _sut.RecordCoreAsync(request);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        var usage = await db.LlmUsages.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(usage.Model, Is.EqualTo("claude-sonnet-4-6"));
            Assert.That(usage.InputTokens, Is.EqualTo(3));
            Assert.That(usage.OutputTokens, Is.EqualTo(176));
            Assert.That(usage.CacheReadTokens, Is.EqualTo(18685));
            Assert.That(usage.CacheCreationTokens, Is.EqualTo(1886));
        });
    }

    [Test]
    public async Task RecordCoreAsync_StreamingLlmCall_LlmUsageLinkedToProxyRequest()
    {
        var request = new ProxyRequest
        {
            Timestamp = DateTime.UtcNow,
            Method = "POST",
            Path = "/v1/messages",
            RequestHeaders = "{}",
            ResponseHeaders = """{"Content-Type": "text/event-stream"}""",
            ResponseBody = StreamingResponseBody,
            ResponseStatusCode = 200,
            DurationMs = 15
        };

        await _sut.RecordCoreAsync(request);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        var proxyRequest = await db.ProxyRequests.SingleAsync();
        var usage = await db.LlmUsages.SingleAsync();

        Assert.That(usage.ProxyRequestId, Is.EqualTo(proxyRequest.Id));
    }

    // ── LlmUsage: not created for non-LLM calls ───────────────────────────────

    [Test]
    public async Task RecordCoreAsync_NonLlmPath_DoesNotSaveLlmUsage()
    {
        var request = new ProxyRequest
        {
            Timestamp = DateTime.UtcNow,
            Method = "POST",
            Path = "/v1/complete",
            RequestHeaders = "{}",
            ResponseHeaders = """{"Content-Type": "application/json"}""",
            ResponseBody = NonStreamingResponseBody,
            ResponseStatusCode = 200,
            DurationMs = 5
        };

        await _sut.RecordCoreAsync(request);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        Assert.That(await db.LlmUsages.AnyAsync(), Is.False);
    }

    [Test]
    public async Task RecordCoreAsync_GetRequestToMessagesPath_DoesNotSaveLlmUsage()
    {
        var request = new ProxyRequest
        {
            Timestamp = DateTime.UtcNow,
            Method = "GET",
            Path = "/v1/messages",
            RequestHeaders = "{}",
            ResponseHeaders = "{}",
            ResponseStatusCode = 200,
            DurationMs = 5
        };

        await _sut.RecordCoreAsync(request);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        Assert.That(await db.LlmUsages.AnyAsync(), Is.False);
    }

    // ── LlmUsage: graceful handling of unparseable responses ──────────────────

    [Test]
    public async Task RecordCoreAsync_LlmCallWithNullResponseBody_StillSavesProxyRequest()
    {
        var request = new ProxyRequest
        {
            Timestamp = DateTime.UtcNow,
            Method = "POST",
            Path = "/v1/messages",
            RequestHeaders = "{}",
            ResponseHeaders = """{"Content-Type": "application/json"}""",
            ResponseBody = null,
            ResponseStatusCode = 200,
            DurationMs = 5
        };

        await _sut.RecordCoreAsync(request);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        Assert.That(await db.ProxyRequests.CountAsync(), Is.EqualTo(1));
        Assert.That(await db.LlmUsages.AnyAsync(), Is.False);
    }

    [Test]
    public async Task RecordCoreAsync_LlmCallWithMalformedResponseBody_StillSavesProxyRequest()
    {
        var request = new ProxyRequest
        {
            Timestamp = DateTime.UtcNow,
            Method = "POST",
            Path = "/v1/messages",
            RequestHeaders = "{}",
            ResponseHeaders = """{"Content-Type": "application/json"}""",
            ResponseBody = "{not valid json}",
            ResponseStatusCode = 200,
            DurationMs = 5
        };

        await _sut.RecordCoreAsync(request);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        Assert.That(await db.ProxyRequests.CountAsync(), Is.EqualTo(1));
        Assert.That(await db.LlmUsages.AnyAsync(), Is.False);
    }

    [Test]
    public async Task RecordCoreAsync_LlmCallWithMalformedResponseBody_LogsWarning()
    {
        var logger = new CapturingLogger<RecordingService>();
        var sut = new RecordingService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            logger);

        var request = new ProxyRequest
        {
            Timestamp = DateTime.UtcNow,
            Method = "POST",
            Path = "/v1/messages",
            RequestHeaders = "{}",
            ResponseHeaders = """{"Content-Type": "application/json"}""",
            ResponseBody = "{not valid json}",
            ResponseStatusCode = 200,
            DurationMs = 5
        };

        await sut.RecordCoreAsync(request);

        Assert.That(logger.HasWarning, Is.True);
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

/// <summary>Records whether any Warning-or-above log entry was emitted.</summary>
file class CapturingLogger<T> : ILogger<T>
{
    public bool HasWarning { get; private set; }

    public IDisposable? BeginScope<TState>(TState _) where TState : notnull => null;
    public bool IsEnabled(LogLevel _) => true;

    public void Log<TState>(LogLevel logLevel, EventId _, TState _2,
        Exception? _3, Func<TState, Exception?, string> _4)
    {
        if (logLevel >= LogLevel.Warning)
            HasWarning = true;
    }
}
