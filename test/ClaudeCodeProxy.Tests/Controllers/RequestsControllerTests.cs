using System.Net;
using System.Text.Json;
using ClaudeCodeProxy.Data;
using ClaudeCodeProxy.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace ClaudeCodeProxy.Tests.Controllers;

/// <summary>
/// Integration tests for <see cref="ClaudeCodeProxy.Controllers.RequestsController"/>
/// hosting the full application in-process via <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// Data is seeded directly into the in-process database via the factory's DI scope.
/// </summary>
[TestFixture]
public class RequestsControllerTests
{
    private SqliteConnection _keepAliveConnection = null!;
    private MockHttpMessageHandler _mockHttp = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _mockHttp = new MockHttpMessageHandler();
        (_factory, _client, _keepAliveConnection) = CreateTestFactory(_mockHttp);
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
        _mockHttp.Dispose();
        _keepAliveConnection.Dispose();
    }

    // ── GET /api/requests ─────────────────────────────────────────────────────

    [Test]
    public async Task GetRequests_Returns200WithEmptyArray_WhenNoData()
    {
        var response = await _client.GetAsync("/api/requests");

        Assert.That((int)response.StatusCode, Is.EqualTo(200));
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Is.EqualTo("[]"));
    }

    [Test]
    public async Task GetRequests_Returns200WithLlmRequests_WhenDataExists()
    {
        var ts = DateTime.UtcNow.AddMinutes(-30);
        await SeedRequestAsync(ts);

        var response = await _client.GetAsync("/api/requests");

        Assert.That((int)response.StatusCode, Is.EqualTo(200));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(doc.RootElement.GetArrayLength(), Is.EqualTo(1));
    }

    [Test]
    public async Task GetRequests_ExcludesNonLlmRequests()
    {
        // Seed a request without LlmUsage (non-LLM call)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        db.ProxyRequests.Add(new ProxyRequest
        {
            Timestamp = DateTime.UtcNow.AddMinutes(-10),
            Method = "GET",
            Path = "/health",
            RequestHeaders = "{}",
            ResponseHeaders = @"{""Content-Type"":""application/json""}",
            ResponseStatusCode = 200,
            DurationMs = 1,
        });
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/requests");

        Assert.That((int)response.StatusCode, Is.EqualTo(200));
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Is.EqualTo("[]"));
    }

    // ── GET /api/requests/{id} ────────────────────────────────────────────────

    [Test]
    public async Task GetRequestById_Returns404_ForUnknownId()
    {
        var response = await _client.GetAsync("/api/requests/99999");

        Assert.That((int)response.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task GetRequestById_Returns200WithDetailDto_ForKnownId()
    {
        var ts = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var seededId = await SeedRequestAsync(ts);

        var response = await _client.GetAsync($"/api/requests/{seededId}");

        Assert.That((int)response.StatusCode, Is.EqualTo(200));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("id").GetInt64(), Is.EqualTo(seededId));
            Assert.That(root.GetProperty("method").GetString(), Is.EqualTo("POST"));
            Assert.That(root.GetProperty("path").GetString(), Is.EqualTo("/v1/messages"));
            Assert.That(root.GetProperty("model").GetString(), Is.EqualTo("claude-sonnet-4-6"));
            Assert.That(root.GetProperty("inputTokens").GetInt32(), Is.EqualTo(100));
            Assert.That(root.GetProperty("outputTokens").GetInt32(), Is.EqualTo(50));
            Assert.That(root.GetProperty("isStreaming").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("requestBody").GetString(), Is.Not.Null);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Seeds a single LLM request with associated LlmUsage and returns its id.</summary>
    private async Task<long> SeedRequestAsync(DateTime timestamp)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();

        var request = new ProxyRequest
        {
            Timestamp = timestamp,
            Method = "POST",
            Path = "/v1/messages",
            RequestHeaders = "{}",
            RequestBody = @"{""model"":""claude-sonnet-4-6"",""messages"":[]}",
            ResponseHeaders = @"{""Content-Type"":""application/json""}",
            ResponseStatusCode = 200,
            DurationMs = 42,
            ResponseBody = @"{""type"":""message""}",
            LlmUsage = new LlmUsage
            {
                Timestamp = timestamp,
                Model = "claude-sonnet-4-6",
                InputTokens = 100,
                OutputTokens = 50,
            }
        };

        db.ProxyRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private static (WebApplicationFactory<Program> factory, HttpClient client, SqliteConnection keepAlive)
        CreateTestFactory(HttpMessageHandler upstreamHandler)
    {
        var dbName = $"requests-ctrl-{Guid.NewGuid():N}";

        var keepAlive = new SqliteConnection($"Data Source={dbName};Mode=Memory;Cache=Shared");
        keepAlive.Open();

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Upstream:BaseUrl"] = "http://mock-upstream",
                        ["ConnectionStrings:DefaultConnection"] =
                            $"Data Source={dbName};Mode=Memory;Cache=Shared",
                        ["Logging:LogLevel:Default"] = "None"
                    }));

                builder.ConfigureTestServices(services =>
                {
                    var existing = services.SingleOrDefault(
                        d => d.ServiceType == typeof(UpstreamOptions));
                    if (existing != null) services.Remove(existing);
                    services.AddSingleton(new UpstreamOptions
                    {
                        BaseUrl = "http://mock-upstream",
                        TimeoutSeconds = 30
                    });

                    services.AddHttpClient("upstream")
                        .ConfigurePrimaryHttpMessageHandler(() => upstreamHandler);
                });
            });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        return (factory, client, keepAlive);
    }
}
