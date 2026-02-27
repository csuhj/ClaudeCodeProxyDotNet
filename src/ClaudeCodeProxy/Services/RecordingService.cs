using System.Text.Json;
using ClaudeCodeProxy.Data;
using ClaudeCodeProxy.Models;

namespace ClaudeCodeProxy.Services;

/// <summary>
/// Persists proxied request/response pairs to the SQLite database and, for
/// Anthropic Messages API calls, extracts and stores LLM token usage.
/// Recording is performed on a background thread so it never blocks the
/// response path back to the client.
/// </summary>
public class RecordingService : IRecordingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecordingService> _logger;

    public RecordingService(IServiceScopeFactory scopeFactory, ILogger<RecordingService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Enqueues a <see cref="ProxyRequest"/> record for background persistence.
    /// Returns immediately; database write failures are logged but never rethrown.
    /// </summary>
    public void Record(ProxyRequest request) => _ = Task.Run(() => RecordCoreAsync(request));

    /// <summary>
    /// Performs the actual persistence. Exposed as <c>internal</c> so tests can
    /// await it directly without racing against a background <see cref="Task.Run"/>.
    /// </summary>
    internal async Task RecordCoreAsync(ProxyRequest request)
    {
        try
        {
            // Attempt token extraction for Anthropic Messages API calls before saving,
            // so EF can cascade-insert the LlmUsage row together with the ProxyRequest.
            if (TokenUsageParser.IsAnthropicMessagesCall(request.Path, request.Method))
            {
                var isStreaming = IsStreamingResponse(request.ResponseHeaders);
                var usage = isStreaming
                    ? TokenUsageParser.ParseStreaming(request.ResponseBody)
                    : TokenUsageParser.ParseNonStreaming(request.ResponseBody);

                if (usage != null)
                {
                    request.LlmUsage = new LlmUsage
                    {
                        Timestamp = request.Timestamp,
                        Model = usage.Model,
                        InputTokens = usage.InputTokens,
                        OutputTokens = usage.OutputTokens,
                        CacheReadTokens = usage.CacheReadTokens,
                        CacheCreationTokens = usage.CacheCreationTokens,
                    };
                }
                else
                {
                    _logger.LogWarning(
                        "Token parsing returned no result for LLM call {Method} {Path}.",
                        request.Method, request.Path);
                }
            }

            // Repository is scoped — create a new scope for each background write.
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRecordingRepository>();
            await repository.AddAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to record proxy request {Method} {Path} — recording will be skipped.",
                request.Method, request.Path);
        }
    }

    /// <summary>
    /// Checks whether the recorded response headers indicate a streaming (SSE) response.
    /// </summary>
    private static bool IsStreamingResponse(string responseHeadersJson)
    {
        if (string.IsNullOrWhiteSpace(responseHeadersJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(responseHeadersJson);
            if (doc.RootElement.TryGetProperty("Content-Type", out var ct))
                return ct.GetString()?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) ?? false;
        }
        catch (JsonException)
        {
            // Ignore malformed headers JSON.
        }

        return false;
    }
}
