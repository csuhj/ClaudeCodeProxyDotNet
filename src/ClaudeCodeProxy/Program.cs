using ClaudeCodeProxy.Data;
using ClaudeCodeProxy.Middleware;
using ClaudeCodeProxy.Models;
using ClaudeCodeProxy.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
// Bind and validate upstream options — fail fast if not configured.
var upstreamOptions = new UpstreamOptions();
builder.Configuration.GetSection(UpstreamOptions.SectionName).Bind(upstreamOptions);

if (string.IsNullOrWhiteSpace(upstreamOptions.BaseUrl))
    throw new InvalidOperationException(
        "Upstream base URL is not configured. Set 'Upstream:BaseUrl' in appsettings.json.");

builder.Services.AddSingleton(upstreamOptions);

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<ProxyDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Recording repository & service ────────────────────────────────────────────
// Repository is scoped (one per request scope) to match ProxyDbContext's lifetime.
builder.Services.AddScoped<IRecordingRepository, RecordingRepository>();

// RecordingService is singleton so the middleware can receive it via constructor
// injection. It uses IServiceScopeFactory internally to create short-lived scopes
// that resolve the scoped IRecordingRepository for each background write.
builder.Services.AddSingleton<IRecordingService, RecordingService>();

// ── Stats service & controllers ───────────────────────────────────────────────
builder.Services.AddScoped<IStatsService, StatsService>();
builder.Services.AddControllers();

// ── HTTP Client ───────────────────────────────────────────────────────────────
// A named client is used so the middleware can request it by name via
// IHttpClientFactory. AutomaticDecompression is disabled so compressed
// responses are forwarded as-is with their original Content-Encoding header.
builder.Services.AddHttpClient("upstream", client =>
{
    client.Timeout = TimeSpan.FromSeconds(upstreamOptions.TimeoutSeconds);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    AutomaticDecompression = System.Net.DecompressionMethods.None
});

// ── App pipeline ──────────────────────────────────────────────────────────────
var app = builder.Build();

// Apply any pending EF Core migrations automatically on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProxyDbContext>();
    db.Database.Migrate();
}

// Map API controllers before the proxy so that /api/stats/* is handled locally.
// ProxyMiddleware checks context.GetEndpoint() and delegates to _next when a
// controller route is matched, so no requests are accidentally proxied upstream.
app.MapControllers();

// ProxyMiddleware is the terminal handler for all requests not matched by a controller.
app.UseMiddleware<ProxyMiddleware>();

app.Run();

// Make the implicit Program class public so WebApplicationFactory<Program> can reference it
// from the test project.
public partial class Program { }
