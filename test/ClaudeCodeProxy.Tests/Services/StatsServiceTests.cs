using ClaudeCodeProxy.Data;
using ClaudeCodeProxy.Models;
using ClaudeCodeProxy.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClaudeCodeProxy.Tests.Services;

/// <summary>
/// Tests for <see cref="StatsService"/> using a real in-memory SQLite database
/// seeded with known data to assert correct bucketing and aggregation.
/// </summary>
[TestFixture]
public class StatsServiceTests
{
    private SqliteConnection _connection = null!;
    private ProxyDbContext _db = null!;
    private RecordingRepository _repository = null!;
    private StatsService _sut = null!;

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

        _repository = new RecordingRepository(_db);
        _sut = new StatsService(_repository);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProxyRequest MakeRequest(DateTime timestamp, LlmUsage? usage = null) =>
        new()
        {
            Timestamp = timestamp,
            Method = "POST",
            Path = "/v1/messages",
            RequestHeaders = "{}",
            ResponseHeaders = "{}",
            ResponseStatusCode = 200,
            DurationMs = 10,
            LlmUsage = usage,
        };

    private static LlmUsage MakeUsage(DateTime timestamp, int input, int output) =>
        new()
        {
            Timestamp = timestamp,
            Model = "claude-sonnet-4-6",
            InputTokens = input,
            OutputTokens = output,
        };

    private async Task SeedAsync(params ProxyRequest[] requests)
    {
        foreach (var request in requests)
            await _repository.AddAsync(request);
    }

    // ── GetRequestsPerHour ────────────────────────────────────────────────────

    [Test]
    public async Task GetRequestsPerHour_ReturnsEmpty_WhenNoDataInRange()
    {
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await _sut.GetRequestsPerHourAsync(from, to);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetRequestsPerHour_ExcludesRequestsOutsideDateRange()
    {
        var inRange = new DateTime(2026, 1, 2, 10, 30, 0, DateTimeKind.Utc);
        var before = new DateTime(2026, 1, 1, 23, 59, 0, DateTimeKind.Utc);
        var after = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc); // equals 'to', exclusive

        await SeedAsync(
            MakeRequest(before),
            MakeRequest(inRange),
            MakeRequest(after));

        var from = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        var result = await _sut.GetRequestsPerHourAsync(from, to);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].RequestCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GetRequestsPerHour_GroupsRequestsIntoCorrectHourBuckets()
    {
        var hour10a = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var hour10b = new DateTime(2026, 1, 1, 10, 45, 0, DateTimeKind.Utc);
        var hour11 = new DateTime(2026, 1, 1, 11, 15, 0, DateTimeKind.Utc);

        await SeedAsync(
            MakeRequest(hour10a),
            MakeRequest(hour10b),
            MakeRequest(hour11));

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await _sut.GetRequestsPerHourAsync(from, to);

        Assert.That(result, Has.Count.EqualTo(2));

        var bucket10 = result.Single(b => b.TimeBucket.Hour == 10);
        var bucket11 = result.Single(b => b.TimeBucket.Hour == 11);

        Assert.Multiple(() =>
        {
            Assert.That(bucket10.RequestCount, Is.EqualTo(2));
            Assert.That(bucket11.RequestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetRequestsPerHour_TimeBucketIsTruncatedToHour()
    {
        var ts = new DateTime(2026, 1, 1, 14, 37, 55, DateTimeKind.Utc);
        await SeedAsync(MakeRequest(ts));

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await _sut.GetRequestsPerHourAsync(from, to);

        Assert.That(result, Has.Count.EqualTo(1));
        var expected = new DateTime(2026, 1, 1, 14, 0, 0, DateTimeKind.Utc);
        Assert.That(result[0].TimeBucket, Is.EqualTo(expected));
    }

    [Test]
    public async Task GetRequestsPerHour_CountsLlmRequestsCorrectly()
    {
        var ts = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

        await SeedAsync(
            MakeRequest(ts, MakeUsage(ts, 100, 50)),
            MakeRequest(ts, MakeUsage(ts, 200, 75)),
            MakeRequest(ts)); // non-LLM request

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await _sut.GetRequestsPerHourAsync(from, to);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result[0].RequestCount, Is.EqualTo(3));
            Assert.That(result[0].LlmRequestCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task GetRequestsPerHour_SumsTokensCorrectly()
    {
        var ts = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

        await SeedAsync(
            MakeRequest(ts, MakeUsage(ts, 100, 50)),
            MakeRequest(ts, MakeUsage(ts, 200, 75)),
            MakeRequest(ts)); // non-LLM, contributes 0 tokens

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await _sut.GetRequestsPerHourAsync(from, to);

        Assert.Multiple(() =>
        {
            Assert.That(result[0].TotalInputTokens, Is.EqualTo(300));
            Assert.That(result[0].TotalOutputTokens, Is.EqualTo(125));
        });
    }

    [Test]
    public async Task GetRequestsPerHour_ReturnsBucketsInAscendingOrder()
    {
        var ts1 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var ts2 = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var ts3 = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);

        await SeedAsync(MakeRequest(ts1), MakeRequest(ts2), MakeRequest(ts3));

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await _sut.GetRequestsPerHourAsync(from, to);

        Assert.That(result.Select(b => b.TimeBucket), Is.Ordered.Ascending);
    }

    // ── GetRequestsPerDay ─────────────────────────────────────────────────────

    [Test]
    public async Task GetRequestsPerDay_ReturnsEmpty_WhenNoDataInRange()
    {
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc);

        var result = await _sut.GetRequestsPerDayAsync(from, to);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetRequestsPerDay_GroupsRequestsByDay()
    {
        var day1a = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var day1b = new DateTime(2026, 1, 1, 20, 30, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);

        await SeedAsync(MakeRequest(day1a), MakeRequest(day1b), MakeRequest(day2));

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        var result = await _sut.GetRequestsPerDayAsync(from, to);

        Assert.That(result, Has.Count.EqualTo(2));

        var day1Bucket = result.Single(b => b.TimeBucket.Day == 1);
        var day2Bucket = result.Single(b => b.TimeBucket.Day == 2);

        Assert.Multiple(() =>
        {
            Assert.That(day1Bucket.RequestCount, Is.EqualTo(2));
            Assert.That(day2Bucket.RequestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetRequestsPerDay_TimeBucketIsTruncatedToMidnight()
    {
        var ts = new DateTime(2026, 3, 15, 14, 37, 55, DateTimeKind.Utc);
        await SeedAsync(MakeRequest(ts));

        var from = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await _sut.GetRequestsPerDayAsync(from, to);

        Assert.That(result, Has.Count.EqualTo(1));
        var expected = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        Assert.That(result[0].TimeBucket, Is.EqualTo(expected));
    }

    [Test]
    public async Task GetRequestsPerDay_SumsTokensAcrossMultipleDayRequests()
    {
        var ts = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        await SeedAsync(
            MakeRequest(ts, MakeUsage(ts, 500, 200)),
            MakeRequest(ts, MakeUsage(ts, 300, 100)),
            MakeRequest(ts)); // non-LLM

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc);

        var result = await _sut.GetRequestsPerDayAsync(from, to);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result[0].RequestCount, Is.EqualTo(3));
            Assert.That(result[0].LlmRequestCount, Is.EqualTo(2));
            Assert.That(result[0].TotalInputTokens, Is.EqualTo(800));
            Assert.That(result[0].TotalOutputTokens, Is.EqualTo(300));
        });
    }

    [Test]
    public async Task GetRequestsPerDay_ReturnsBucketsInAscendingOrder()
    {
        var day3 = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var day1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        await SeedAsync(MakeRequest(day3), MakeRequest(day1), MakeRequest(day2));

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc);

        var result = await _sut.GetRequestsPerDayAsync(from, to);

        Assert.That(result.Select(b => b.TimeBucket), Is.Ordered.Ascending);
    }
}
