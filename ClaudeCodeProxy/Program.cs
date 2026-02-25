using ClaudeCodeProxy.Middleware;
using ClaudeCodeProxy.Models;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
// Allow ANTHROPIC_BASE_URL environment variable to override Upstream:BaseUrl.
// This matches the env var name that Claude Code itself uses.
var anthropicBaseUrl = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL");
if (!string.IsNullOrWhiteSpace(anthropicBaseUrl))
    builder.Configuration["Upstream:BaseUrl"] = anthropicBaseUrl;

// Bind and validate upstream options — fail fast if not configured.
var upstreamOptions = new UpstreamOptions();
builder.Configuration.GetSection(UpstreamOptions.SectionName).Bind(upstreamOptions);

if (string.IsNullOrWhiteSpace(upstreamOptions.BaseUrl))
    throw new InvalidOperationException(
        "Upstream base URL is not configured. " +
        "Set 'Upstream:BaseUrl' in appsettings.json or the ANTHROPIC_BASE_URL environment variable.");

builder.Services.AddSingleton(upstreamOptions);

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

// ProxyMiddleware is the terminal handler — every request is forwarded upstream.
app.UseMiddleware<ProxyMiddleware>();

app.Run();
