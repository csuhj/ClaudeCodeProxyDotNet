using System.Net;
using ClaudeCodeProxy.Data;
using ClaudeCodeProxy.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace ClaudeCodeProxy.Tests;

/// <summary>
/// End-to-end tests that host the full application in-process via
/// <see cref="WebApplicationFactory{TEntryPoint}"/> and drive HTTP requests through
/// the complete proxy pipeline. A <see cref="MockHttpMessageHandler"/> (or a custom
/// throwing handler) replaces the real upstream, and a named in-memory SQLite database
/// replaces the file-based one.
/// </summary>
[TestFixture]
public class EndToEndTests
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

    [Test]
    public async Task ProxyForwardsRequestAndReturnsUpstreamResponse()
    {
        _mockHttp.When("http://mock-upstream/v1/messages")
            .Respond(HttpStatusCode.OK, "application/json", """{"type":"message"}""");

        var response = await _client.GetAsync("/v1/messages");

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(response.Content.Headers.ContentType?.MediaType,
                Is.EqualTo("application/json"));
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("message"));
    }

    [Test]
    public async Task ProxyRecordsRequestInDatabase()
    {
        _mockHttp.When("http://mock-upstream/v1/messages")
            .Respond(HttpStatusCode.OK, "application/json", "{}");

        await _client.GetAsync("/v1/messages");

        // Recording is fire-and-forget — allow a moment for the background task to complete.
        await Task.Delay(200);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
        var record = await db.ProxyRequests.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(record.Method, Is.EqualTo("GET"));
            Assert.That(record.Path, Is.EqualTo("/v1/messages"));
            Assert.That(record.ResponseStatusCode, Is.EqualTo(200));
        });
    }

    [Test]
    public async Task ProxyReturns502WhenUpstreamIsUnreachable()
    {
        // MockHttpMessageHandler doesn't propagate faulted tasks reliably, so use a
        // dedicated handler that directly throws HttpRequestException for this test.
        using var throwingHandler = new ExceptionHttpMessageHandler(
            new HttpRequestException("Connection refused"));

        var (factory, client, keepAlive) = CreateTestFactory(throwingHandler);
        using (keepAlive)
        using (factory)
        using (client)
        {
            var response = await client.GetAsync("/v1/messages");
            Assert.That((int)response.StatusCode, Is.EqualTo(502));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="WebApplicationFactory{TEntryPoint}"/> wired up to the
    /// supplied <paramref name="upstreamHandler"/> and an isolated in-memory database.
    /// </summary>
    private static (WebApplicationFactory<Program> factory, HttpClient client, SqliteConnection keepAlive)
        CreateTestFactory(HttpMessageHandler upstreamHandler)
    {
        // A unique name ensures parallel test runs don't share database state.
        var dbName = $"e2e-{Guid.NewGuid():N}";

        // Keep a connection open so the named in-memory database persists for the
        // lifetime of the returned factory.
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
                    // Program.cs binds UpstreamOptions eagerly before ConfigureAppConfiguration
                    // overrides are visible, so we replace the singleton directly here.
                    var existing = services.SingleOrDefault(
                        d => d.ServiceType == typeof(UpstreamOptions));
                    if (existing != null) services.Remove(existing);
                    services.AddSingleton(new UpstreamOptions
                    {
                        BaseUrl = "http://mock-upstream",
                        TimeoutSeconds = 30
                    });

                    // Override the "upstream" client's primary handler so requests are
                    // intercepted instead of hitting the real network.
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

    /// <summary>
    /// An <see cref="HttpMessageHandler"/> that always faults with the given exception,
    /// simulating an unreachable upstream.
    /// </summary>
    private sealed class ExceptionHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }
}
