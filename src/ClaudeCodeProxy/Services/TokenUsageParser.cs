using System.Text.Json;
using ClaudeCodeProxy.Models;

namespace ClaudeCodeProxy.Services;

/// <summary>
/// Parses Anthropic Messages API responses to extract LLM token usage.
/// Supports both non-streaming (JSON) and streaming (SSE / text/event-stream) responses.
/// </summary>
public static class TokenUsageParser
{
    /// <summary>
    /// Returns true when the request path and method identify an Anthropic Messages API call.
    /// Strips any query string before checking so paths like <c>/v1/messages?foo=bar</c> match.
    /// </summary>
    public static bool IsAnthropicMessagesCall(string path, string method)
    {
        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return false;

        // Strip query string for the path comparison.
        var qIndex = path.IndexOf('?');
        var pathOnly = qIndex >= 0 ? path[..qIndex] : path;

        // Accept /v1/messages (with or without a path prefix) and the shorter /messages suffix.
        return pathOnly.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase)
            || pathOnly.EndsWith("/messages", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses a non-streaming Anthropic Messages API response body.
    /// Expects the top-level JSON to contain a <c>usage</c> object and a <c>model</c> string.
    /// Returns <c>null</c> if the body is null/empty or cannot be parsed.
    /// </summary>
    public static TokenUsageResult? ParseNonStreaming(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("usage", out var usage))
                return null;

            return new TokenUsageResult
            {
                Model = root.TryGetProperty("model", out var model) ? model.GetString() : null,
                InputTokens = ReadInt(usage, "input_tokens"),
                OutputTokens = ReadInt(usage, "output_tokens"),
                CacheReadTokens = ReadInt(usage, "cache_read_input_tokens"),
                CacheCreationTokens = ReadInt(usage, "cache_creation_input_tokens"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a streaming (SSE / <c>text/event-stream</c>) Anthropic Messages API response body.
    /// <para>
    /// Scans the newline-delimited <c>data:</c> lines for two event types:
    /// <list type="bullet">
    ///   <item><c>message_start</c> — provides the model name and initial input token counts.</item>
    ///   <item><c>message_delta</c> — provides the final cumulative token counts including output tokens.</item>
    /// </list>
    /// When both events are present the <c>message_delta</c> usage values take precedence for
    /// all token counts (Anthropic includes cumulative totals there), while the model name comes
    /// from <c>message_start</c>.
    /// </para>
    /// Returns <c>null</c> if the body is null/empty or neither required event is found.
    /// </summary>
    public static TokenUsageResult? ParseStreaming(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        string? model = null;
        TokenUsageResult? startUsage = null;
        TokenUsageResult? deltaUsage = null;

        foreach (var line in responseBody.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var json = trimmed["data:".Length..].Trim();
            if (string.IsNullOrEmpty(json) || json == "[DONE]")
                continue;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp))
                    continue;

                var eventType = typeProp.GetString();

                if (eventType == "message_start")
                {
                    // message_start carries the model and initial input token counts.
                    if (root.TryGetProperty("message", out var message))
                    {
                        model = message.TryGetProperty("model", out var m) ? m.GetString() : null;

                        if (message.TryGetProperty("usage", out var usage))
                        {
                            startUsage = new TokenUsageResult
                            {
                                Model = model,
                                InputTokens = ReadInt(usage, "input_tokens"),
                                OutputTokens = ReadInt(usage, "output_tokens"),
                                CacheReadTokens = ReadInt(usage, "cache_read_input_tokens"),
                                CacheCreationTokens = ReadInt(usage, "cache_creation_input_tokens"),
                            };
                        }
                    }
                }
                else if (eventType == "message_delta")
                {
                    // message_delta carries final cumulative token counts (including output).
                    if (root.TryGetProperty("usage", out var usage))
                    {
                        deltaUsage = new TokenUsageResult
                        {
                            Model = model, // set below after the loop if we already have it
                            InputTokens = ReadInt(usage, "input_tokens"),
                            OutputTokens = ReadInt(usage, "output_tokens"),
                            CacheReadTokens = ReadInt(usage, "cache_read_input_tokens"),
                            CacheCreationTokens = ReadInt(usage, "cache_creation_input_tokens"),
                        };
                    }
                }
            }
            catch (JsonException)
            {
                // Skip malformed data lines and keep scanning.
            }
        }

        if (deltaUsage != null)
        {
            // Prefer model from message_start; fall back to any model parsed along the way.
            deltaUsage.Model = model ?? deltaUsage.Model;
            return deltaUsage;
        }

        return startUsage; // Fall back to message_start data if no message_delta was found.
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int ReadInt(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();
        return 0;
    }
}
