using ClaudeCodeProxy.Data;
using ClaudeCodeProxy.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClaudeCodeProxy.Tests.Data;

/// <summary>
/// Tests for the new LLM-request query methods added to <see cref="RecordingRepository"/>
/// in Version 2: <c>GetLlmRequestsAsync</c> and <c>GetLlmRequestByIdAsync</c>.
/// </summary>
[TestFixture]
public class RecordingRepositoryLlmRequestTests
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProxyRequest MakeRequest(
        DateTime timestamp,
        string responseHeaders = @"{""Content-Type"":""application/json""}",
        LlmUsage? usage = null) =>
        new()
        {
            Timestamp = timestamp,
            Method = "POST",
            Path = "/v1/messages",
            RequestHeaders = "{}",
            RequestBody = @"{""model"":""claude-sonnet-4-6"",""messages"":[]}",
            ResponseHeaders = responseHeaders,
            ResponseStatusCode = 200,
            DurationMs = 10,
            ResponseBody = @"{""type"":""message""}",
            LlmUsage = usage,
        };

    private static LlmUsage MakeUsage(DateTime timestamp) =>
        new()
        {
            Timestamp = timestamp,
            Model = "claude-sonnet-4-6",
            InputTokens = 100,
            OutputTokens = 50,
        };

    private async Task SeedAsync(params ProxyRequest[] requests)
    {
        foreach (var r in requests)
            await _sut.AddAsync(r);
    }

    // ── GetLlmRequestsAsync ───────────────────────────────────────────────────

    [Test]
    public async Task GetLlmRequestsAsync_ReturnsOnlyRequestsWithLlmUsage()
    {
        var ts = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        await SeedAsync(
            MakeRequest(ts, usage: MakeUsage(ts)),  // LLM call — should appear
            MakeRequest(ts));                        // non-LLM call — should be excluded

        var result = await _sut.GetLlmRequestsAsync(ts.AddHours(-1), ts.AddHours(1), 0, 50);

        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetLlmRequestsAsync_RespectsHalfOpenTimeWindow()
    {
        var inRange = new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc);
        var atToExclusive = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        var from = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var to = atToExclusive;

        await SeedAsync(
            MakeRequest(inRange, usage: MakeUsage(inRange)),
            MakeRequest(before, usage: MakeUsage(before)),
            MakeRequest(atToExclusive, usage: MakeUsage(atToExclusive)));

        var result = await _sut.GetLlmRequestsAsync(from, to, 0, 50);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Timestamp, Is.EqualTo(inRange));
    }

    [Test]
    public async Task GetLlmRequestsAsync_ReturnsResultsNewestFirst()
    {
        var ts1 = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var ts2 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var ts3 = new DateTime(2026, 1, 1, 16, 0, 0, DateTimeKind.Utc);

        await SeedAsync(
            MakeRequest(ts1, usage: MakeUsage(ts1)),
            MakeRequest(ts2, usage: MakeUsage(ts2)),
            MakeRequest(ts3, usage: MakeUsage(ts3)));

        var result = await _sut.GetLlmRequestsAsync(ts1.AddHours(-1), ts3.AddHours(1), 0, 50);

        Assert.That(result.Select(r => r.Timestamp), Is.Ordered.Descending);
    }

    [Test]
    public async Task GetLlmRequestsAsync_PaginationSkipsAndTakesCorrectly()
    {
        // Seed 5 requests with timestamps at hours 0–4 on the same day.
        var timestamps = Enumerable.Range(0, 5)
            .Select(i => new DateTime(2026, 1, 1, i, 0, 0, DateTimeKind.Utc))
            .ToList();

        foreach (var ts in timestamps)
            await _sut.AddAsync(MakeRequest(ts, usage: MakeUsage(ts)));

        // Descending order: hour4, hour3, hour2, hour1, hour0
        // skip=2, take=2 → hour2, hour1
        var result = await _sut.GetLlmRequestsAsync(
            timestamps.First().AddHours(-1), timestamps.Last().AddHours(1), skip: 2, take: 2);

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].Timestamp, Is.EqualTo(timestamps[2])); // hour2
            Assert.That(result[1].Timestamp, Is.EqualTo(timestamps[1])); // hour1
        });
    }

    // ── GetLlmRequestByIdAsync ────────────────────────────────────────────────

    [Test]
    public async Task GetLlmRequestByIdAsync_ReturnsNull_ForUnknownId()
    {
        var result = await _sut.GetLlmRequestByIdAsync(99999);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetLlmRequestByIdAsync_ReturnsAllFields_ForKnownId()
    {
        var ts = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var request = MakeRequest(ts, usage: MakeUsage(ts));
        request.RequestBody = @"{""model"":""claude-sonnet-4-6""}";
        request.ResponseBody = @"{""type"":""message""}";
        await _sut.AddAsync(request);

        var result = await _sut.GetLlmRequestByIdAsync(request.Id);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Id, Is.EqualTo(request.Id));
            Assert.That(result.Timestamp, Is.EqualTo(ts));
            Assert.That(result.Method, Is.EqualTo("POST"));
            Assert.That(result.Path, Is.EqualTo("/v1/messages"));
            Assert.That(result.RequestBody, Is.EqualTo(@"{""model"":""claude-sonnet-4-6""}"));
            Assert.That(result.ResponseBody, Is.EqualTo(@"{""type"":""message""}"));
            Assert.That(result.RequestHeaders, Is.EqualTo("{}"));
            Assert.That(result.Model, Is.EqualTo("claude-sonnet-4-6"));
            Assert.That(result.InputTokens, Is.EqualTo(100));
            Assert.That(result.OutputTokens, Is.EqualTo(50));
            Assert.That(result.DurationMs, Is.EqualTo(10));
            Assert.That(result.ResponseStatusCode, Is.EqualTo(200));
        });
    }

    [Test]
    public async Task GetLlmRequestByIdAsync_IsStreamingTrue_WhenContentTypeIsEventStream()
    {
        var ts = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var sseHeaders = @"{""Content-Type"":""text/event-stream""}";
        await _sut.AddAsync(MakeRequest(ts, sseHeaders, MakeUsage(ts)));

        var saved = await _db.ProxyRequests.SingleAsync();
        var result = await _sut.GetLlmRequestByIdAsync(saved.Id);

        Assert.That(result!.IsStreaming, Is.True);
    }

    [Test]
    public async Task GetLlmRequestByIdAsync_IsStreamingFalse_WhenContentTypeIsApplicationJson()
    {
        var ts = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var jsonHeaders = @"{""Content-Type"":""application/json""}";
        await _sut.AddAsync(MakeRequest(ts, jsonHeaders, MakeUsage(ts)));

        var saved = await _db.ProxyRequests.SingleAsync();
        var result = await _sut.GetLlmRequestByIdAsync(saved.Id);

        Assert.That(result!.IsStreaming, Is.False);
    }
}
