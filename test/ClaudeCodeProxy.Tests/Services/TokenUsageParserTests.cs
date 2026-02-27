using ClaudeCodeProxy.Services;

namespace ClaudeCodeProxy.Tests.Services;

[TestFixture]
public class TokenUsageParserTests
{
    // ── IsAnthropicMessagesCall ────────────────────────────────────────────────

    [TestCase("/v1/messages", "POST", true)]
    [TestCase("/v1/messages?stream=true", "POST", true)]
    [TestCase("/prefix/v1/messages", "POST", true)]
    [TestCase("/messages", "POST", true)]
    [TestCase("/api/messages", "POST", true)]
    [TestCase("/v1/messages", "GET", false, Description = "Wrong method")]
    [TestCase("/v1/messages", "get", false, Description = "Wrong method (lowercase)")]
    [TestCase("/v1/other", "POST", false, Description = "Wrong path")]
    [TestCase("/v1/messages-extended", "POST", false, Description = "Path suffix does not match")]
    [TestCase("", "POST", false, Description = "Empty path")]
    public void IsAnthropicMessagesCall_ReturnsExpectedResult(string path, string method, bool expected)
    {
        Assert.That(TokenUsageParser.IsAnthropicMessagesCall(path, method), Is.EqualTo(expected));
    }

    // ── ParseNonStreaming ──────────────────────────────────────────────────────

    [Test]
    public void ParseNonStreaming_FullResponse_ExtractsAllFields()
    {
        const string json = """
            {
              "type": "message",
              "id": "msg_01XFDUDYJgAACzvnptvVoYEL",
              "model": "claude-sonnet-4-6",
              "role": "assistant",
              "content": [{"type": "text", "text": "Hello!"}],
              "stop_reason": "end_turn",
              "usage": {
                "input_tokens": 10,
                "output_tokens": 25,
                "cache_read_input_tokens": 100,
                "cache_creation_input_tokens": 50
              }
            }
            """;

        var result = TokenUsageParser.ParseNonStreaming(json);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Model, Is.EqualTo("claude-sonnet-4-6"));
            Assert.That(result.InputTokens, Is.EqualTo(10));
            Assert.That(result.OutputTokens, Is.EqualTo(25));
            Assert.That(result.CacheReadTokens, Is.EqualTo(100));
            Assert.That(result.CacheCreationTokens, Is.EqualTo(50));
        });
    }

    [Test]
    public void ParseNonStreaming_MissingCacheFields_DefaultsToZero()
    {
        const string json = """
            {
              "type": "message",
              "model": "claude-haiku-4-5-20251001",
              "usage": {
                "input_tokens": 5,
                "output_tokens": 12
              }
            }
            """;

        var result = TokenUsageParser.ParseNonStreaming(json);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.CacheReadTokens, Is.EqualTo(0));
            Assert.That(result.CacheCreationTokens, Is.EqualTo(0));
        });
    }

    [Test]
    public void ParseNonStreaming_NoUsageProperty_ReturnsNull()
    {
        const string json = """{"type": "error", "error": {"type": "invalid_request"}}""";
        Assert.That(TokenUsageParser.ParseNonStreaming(json), Is.Null);
    }

    [Test]
    public void ParseNonStreaming_NullBody_ReturnsNull()
    {
        Assert.That(TokenUsageParser.ParseNonStreaming(null), Is.Null);
    }

    [Test]
    public void ParseNonStreaming_EmptyBody_ReturnsNull()
    {
        Assert.That(TokenUsageParser.ParseNonStreaming(""), Is.Null);
        Assert.That(TokenUsageParser.ParseNonStreaming("   "), Is.Null);
    }

    [Test]
    public void ParseNonStreaming_MalformedJson_ReturnsNull()
    {
        Assert.That(TokenUsageParser.ParseNonStreaming("{not valid json}"), Is.Null);
    }

    // ── ParseStreaming ─────────────────────────────────────────────────────────

    private const string ValidSseBody =
        "event: message_start\n" +
        """data: {"type":"message_start","message":{"model":"claude-sonnet-4-6","id":"msg_01XFD","type":"message","role":"assistant","content":[],"stop_reason":null,"usage":{"input_tokens":3,"cache_creation_input_tokens":1886,"cache_read_input_tokens":18685,"output_tokens":0}}}""" + "\n" +
        "\n" +
        "event: content_block_start\n" +
        """data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""" + "\n" +
        "\n" +
        "event: content_block_delta\n" +
        """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello!"}}""" + "\n" +
        "\n" +
        "event: content_block_stop\n" +
        """data: {"type":"content_block_stop","index":0}""" + "\n" +
        "\n" +
        "event: message_delta\n" +
        """data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"input_tokens":3,"cache_creation_input_tokens":1886,"cache_read_input_tokens":18685,"output_tokens":176}}""" + "\n" +
        "\n" +
        "event: message_stop\n" +
        """data: {"type":"message_stop"}""" + "\n";

    [Test]
    public void ParseStreaming_ValidSseBody_ExtractsAllFields()
    {
        var result = TokenUsageParser.ParseStreaming(ValidSseBody);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Model, Is.EqualTo("claude-sonnet-4-6"));
            Assert.That(result.InputTokens, Is.EqualTo(3));
            Assert.That(result.OutputTokens, Is.EqualTo(176));
            Assert.That(result.CacheReadTokens, Is.EqualTo(18685));
            Assert.That(result.CacheCreationTokens, Is.EqualTo(1886));
        });
    }

    [Test]
    public void ParseStreaming_MessageDeltaTokensTakePrecedenceOverMessageStart()
    {
        // message_start has output_tokens: 0; message_delta has output_tokens: 99.
        // The delta values must win.
        const string body =
            "event: message_start\n" +
            """data: {"type":"message_start","message":{"model":"claude-sonnet-4-6","usage":{"input_tokens":10,"output_tokens":0,"cache_read_input_tokens":0,"cache_creation_input_tokens":0}}}""" + "\n" +
            "\n" +
            "event: message_delta\n" +
            """data: {"type":"message_delta","delta":{},"usage":{"input_tokens":10,"output_tokens":99,"cache_read_input_tokens":5,"cache_creation_input_tokens":2}}""" + "\n";

        var result = TokenUsageParser.ParseStreaming(body);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.OutputTokens, Is.EqualTo(99));
            Assert.That(result.InputTokens, Is.EqualTo(10));
            Assert.That(result.CacheReadTokens, Is.EqualTo(5));
            Assert.That(result.CacheCreationTokens, Is.EqualTo(2));
        });
    }

    [Test]
    public void ParseStreaming_NoMessageDelta_FallsBackToMessageStart()
    {
        // Only a message_start event — delta is absent.
        const string body =
            "event: message_start\n" +
            """data: {"type":"message_start","message":{"model":"claude-opus-4-6","usage":{"input_tokens":7,"output_tokens":0,"cache_read_input_tokens":0,"cache_creation_input_tokens":0}}}""" + "\n";

        var result = TokenUsageParser.ParseStreaming(body);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Model, Is.EqualTo("claude-opus-4-6"));
            Assert.That(result.InputTokens, Is.EqualTo(7));
        });
    }

    [Test]
    public void ParseStreaming_ModelComesFromMessageStart_EvenWhenDeltaPresent()
    {
        // The model field only appears in message_start; message_delta has no model.
        var result = TokenUsageParser.ParseStreaming(ValidSseBody);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Model, Is.EqualTo("claude-sonnet-4-6"));
    }

    [Test]
    public void ParseStreaming_NullBody_ReturnsNull()
    {
        Assert.That(TokenUsageParser.ParseStreaming(null), Is.Null);
    }

    [Test]
    public void ParseStreaming_EmptyBody_ReturnsNull()
    {
        Assert.That(TokenUsageParser.ParseStreaming(""), Is.Null);
        Assert.That(TokenUsageParser.ParseStreaming("   "), Is.Null);
    }

    [Test]
    public void ParseStreaming_BodyWithNoRelevantEvents_ReturnsNull()
    {
        const string body =
            "event: ping\n" +
            """data: {"type":"ping"}""" + "\n";

        Assert.That(TokenUsageParser.ParseStreaming(body), Is.Null);
    }

    [Test]
    public void ParseStreaming_MalformedDataLines_AreSkippedGracefully()
    {
        const string body =
            "event: message_start\n" +
            "data: {INVALID_JSON\n" +
            "\n" +
            "event: message_delta\n" +
            """data: {"type":"message_delta","delta":{},"usage":{"input_tokens":1,"output_tokens":2,"cache_read_input_tokens":0,"cache_creation_input_tokens":0}}""" + "\n";

        // The malformed message_start should be skipped; message_delta (with no model) still parsed.
        var result = TokenUsageParser.ParseStreaming(body);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.OutputTokens, Is.EqualTo(2));
            Assert.That(result.Model, Is.Null);
        });
    }
}
