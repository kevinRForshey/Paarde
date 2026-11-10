using System.Linq;
using System.Net;
using System.Text;

using Paarde.Core.Models;
using Paarde.Core.Services;

using Newtonsoft.Json.Linq;

namespace Paarde.Core.Tests.Services;

// Stands a fake HttpMessageHandler in for the real api.anthropic.com endpoint (same approach
// ClaudeCliRunnerTests takes with a fake CLI script) so streaming SSE parsing can be exercised
// without a real, paid API call.
public class AnthropicApiRunnerTests
{
    private const string StreamedResponse = """
        data: {"type":"message_start","message":{"usage":{"input_tokens":10}}}

        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}

        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":", world"}}

        data: {"type":"content_block_stop","index":0}

        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":5}}

        data: {"type":"message_stop"}

        """;

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastRequestBody { get; private set; }

        // A tool-use round trip makes more than one call per
        // RunPromptAsync - this records each request body in order, so a test can assert on, say,
        // the second request's tool_result content without the first call's body overwriting it.
        public List<string> RequestBodies { get; } = new();

        public HttpStatusCode StatusCodeToReturn { get; set; } = HttpStatusCode.OK;

        public string ResponseBodyToReturn { get; set; } = StreamedResponse;

        // When non-empty, each call dequeues the next response instead of always returning
        // ResponseBodyToReturn - lets a test drive a multi-turn tool-use loop, one scripted
        // response per turn.
        public Queue<string> ResponseBodyQueue { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(LastRequestBody ?? string.Empty);

            var responseBody = ResponseBodyQueue.Count > 0 ? ResponseBodyQueue.Dequeue() : ResponseBodyToReturn;

            return new HttpResponseMessage(StatusCodeToReturn)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    private readonly FakeHttpMessageHandler _handler = new();
    private readonly AnthropicApiOptions _options = new("test-api-key", "claude-sonnet-5");
    private AnthropicApiRunner CreateRunner(AnthropicApiOptions? options = null) =>
        new(() => Task.FromResult(options ?? _options), new HttpClient(_handler));

    [Fact]
    public async Task RunPromptAsync_StreamedTextDeltas_AccumulatesThemAsOutput()
    {
        var runner = CreateRunner();

        var result = await runner.RunPromptAsync("prompt");

        Assert.True(result.Success);
        Assert.Equal("Hello, world", result.Output);
    }

    [Fact]
    public async Task RunPromptAsync_StreamedTextDeltas_InvokesOnTextDeltaForEachChunkInOrder()
    {
        var runner = CreateRunner();
        var received = new List<string>();

        await runner.RunPromptAsync("prompt", onTextDelta: received.Add);

        Assert.Equal(new[] { "Hello", ", world" }, received);
    }

    [Fact]
    public async Task RunPromptAsync_MessageDeltaLine_PopulatesUsageFields()
    {
        var runner = CreateRunner();

        var result = await runner.RunPromptAsync("prompt");

        Assert.Equal(10, result.InputTokens);
        Assert.Equal(5, result.OutputTokens);
        Assert.Null(result.TotalCostUsd);
        Assert.Null(result.ExitCode);
        Assert.NotNull(result.SessionId);
    }

    [Fact]
    public async Task RunPromptAsync_SendsModelAndApiKeyFromOptions()
    {
        var runner = CreateRunner();

        await runner.RunPromptAsync("prompt");

        Assert.Equal("test-api-key", _handler.LastRequest!.Headers.GetValues("x-api-key").Single());
        var body = JObject.Parse(_handler.LastRequestBody!);
        Assert.Equal("claude-sonnet-5", body["model"]!.ToString());
    }

    [Fact]
    public async Task RunPromptAsync_NoApiKeyConfigured_ReturnsFailureWithoutMakingARequest()
    {
        var runner = CreateRunner(new AnthropicApiOptions(string.Empty, "claude-sonnet-5"));

        var result = await runner.RunPromptAsync("prompt");

        Assert.False(result.Success);
        Assert.Null(_handler.LastRequest);
    }

    [Fact]
    public async Task RunPromptAsync_NonSuccessStatusCode_ReturnsFailureWithTheApisErrorMessage()
    {
        _handler.StatusCodeToReturn = HttpStatusCode.Unauthorized;
        _handler.ResponseBodyToReturn = """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""";
        var runner = CreateRunner();

        var result = await runner.RunPromptAsync("prompt");

        Assert.False(result.Success);
        Assert.Equal("invalid x-api-key", result.ErrorMessage);
    }

    [Fact]
    public async Task RunPromptAsync_MaxTokensStopReason_ReturnsFailureButKeepsThePartialOutput()
    {
        _handler.ResponseBodyToReturn = """
            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Truncated"}}

            data: {"type":"content_block_stop","index":0}

            data: {"type":"message_delta","delta":{"stop_reason":"max_tokens"},"usage":{"output_tokens":8192}}

            """;
        var runner = CreateRunner();

        var result = await runner.RunPromptAsync("prompt");

        Assert.False(result.Success);
        Assert.Equal("Truncated", result.Output);
    }

    [Fact]
    public async Task RunPromptAsync_NoResumeSessionIdGiven_SendsOnlyTheNewUserMessage()
    {
        var runner = CreateRunner();

        await runner.RunPromptAsync("first prompt");

        var messages = (JArray)JObject.Parse(_handler.LastRequestBody!)["messages"]!;
        Assert.Single(messages);
        Assert.Equal("first prompt", messages[0]["content"]!.ToString());
    }

    [Fact]
    public async Task RunPromptAsync_ResumeSessionIdGiven_IncludesPriorTurnsInTheRequest()
    {
        var runner = CreateRunner();

        var first = await runner.RunPromptAsync("first prompt");
        await runner.RunPromptAsync("second prompt", resumeSessionId: first.SessionId);

        var messages = (JArray)JObject.Parse(_handler.LastRequestBody!)["messages"]!;
        Assert.Equal(3, messages.Count);
        Assert.Equal("first prompt", messages[0]["content"]!.ToString());
        Assert.Equal("user", messages[0]["role"]!.ToString());
        Assert.Equal("assistant", messages[1]["role"]!.ToString());
        Assert.Equal("second prompt", messages[2]["content"]!.ToString());
    }

    [Fact]
    public async Task RunPromptAsync_UnknownResumeSessionId_StartsFreshHistoryAndReturnsANewSessionId()
    {
        var runner = CreateRunner();

        var result = await runner.RunPromptAsync("prompt", resumeSessionId: "never-seen-before");

        var messages = (JArray)JObject.Parse(_handler.LastRequestBody!)["messages"]!;
        Assert.Single(messages);
        Assert.NotEqual("never-seen-before", result.SessionId);
    }

    [Fact]
    public async Task RunPromptAsync_AgentToolsDisabled_DoesNotSendToolsToTheApi()
    {
        var runner = CreateRunner();

        await runner.RunPromptAsync("prompt");

        var body = JObject.Parse(_handler.LastRequestBody!);
        Assert.Null(body["tools"]);
    }

    [Fact]
    public async Task RunPromptAsync_AgentToolsEnabled_SendsToolsToTheApi()
    {
        var runner = CreateRunner(new AnthropicApiOptions("test-api-key", "claude-sonnet-5", AgentToolsEnabled: true));

        await runner.RunPromptAsync("prompt");

        var body = JObject.Parse(_handler.LastRequestBody!);
        var toolNames = ((JArray)body["tools"]!).Select(tool => tool["name"]!.ToString()).ToList();
        Assert.Contains("read_file", toolNames);
        Assert.Contains("grep", toolNames);
        Assert.Contains("glob", toolNames);
    }

    [Fact]
    public async Task RunPromptAsync_ModelRequestsReadFile_ExecutesItAndSendsTheToolResultBackThenReturnsTheFollowUpAnswer()
    {
        using var workingDirectory = new TempDirectory();
        File.WriteAllText(Path.Combine(workingDirectory.Path, "foo.txt"), "file contents");

        const string ToolUseResponse = """
            data: {"type":"message_start","message":{"usage":{"input_tokens":10}}}

            data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_1","name":"read_file","input":{}}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"path\":\"foo.txt\"}"}}

            data: {"type":"content_block_stop","index":0}

            data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":5}}

            data: {"type":"message_stop"}

            """;
        _handler.ResponseBodyQueue.Enqueue(ToolUseResponse);
        _handler.ResponseBodyQueue.Enqueue(StreamedResponse);
        var runner = CreateRunner(new AnthropicApiOptions("test-api-key", "claude-sonnet-5", AgentToolsEnabled: true));

        var result = await runner.RunPromptAsync("prompt", workingDirectory.Path);

        Assert.True(result.Success);
        Assert.Equal("Hello, world", result.Output);
        Assert.Equal(2, _handler.RequestBodies.Count);

        var secondRequestMessages = (JArray)JObject.Parse(_handler.RequestBodies[1])["messages"]!;
        var toolResultMessage = secondRequestMessages[2];
        Assert.Equal("user", toolResultMessage["role"]!.ToString());
        var toolResult = toolResultMessage["content"]![0]!;
        Assert.Equal("toolu_1", toolResult["tool_use_id"]!.ToString());
        Assert.Equal("file contents", toolResult["content"]!.ToString());
        Assert.False(toolResult["is_error"]!.ToObject<bool>());
    }

    [Fact]
    public async Task RunPromptAsync_ModelRequestsReadFile_InvokesOnToolActivityWithADescription()
    {
        using var workingDirectory = new TempDirectory();
        File.WriteAllText(Path.Combine(workingDirectory.Path, "foo.txt"), "file contents");

        const string ToolUseResponse = """
            data: {"type":"message_start","message":{"usage":{"input_tokens":10}}}

            data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_1","name":"read_file","input":{}}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"path\":\"foo.txt\"}"}}

            data: {"type":"content_block_stop","index":0}

            data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":5}}

            data: {"type":"message_stop"}

            """;
        _handler.ResponseBodyQueue.Enqueue(ToolUseResponse);
        _handler.ResponseBodyQueue.Enqueue(StreamedResponse);
        var runner = CreateRunner(new AnthropicApiOptions("test-api-key", "claude-sonnet-5", AgentToolsEnabled: true));
        var toolActivities = new List<string>();

        await runner.RunPromptAsync("prompt", workingDirectory.Path, onToolActivity: toolActivities.Add);

        Assert.Equal(new[] { "Reading `foo.txt`" }, toolActivities);
    }

    [Fact]
    public async Task RunPromptAsync_ModelRequestsReadFileOutsideTheWorkingDirectory_RejectsItAsAToolErrorInsteadOfThrowing()
    {
        using var workingDirectory = new TempDirectory();

        const string ToolUseResponse = """
            data: {"type":"message_start","message":{"usage":{"input_tokens":10}}}

            data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_1","name":"read_file","input":{}}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"path\":\"../outside.txt\"}"}}

            data: {"type":"content_block_stop","index":0}

            data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":5}}

            data: {"type":"message_stop"}

            """;
        _handler.ResponseBodyQueue.Enqueue(ToolUseResponse);
        _handler.ResponseBodyQueue.Enqueue(StreamedResponse);
        var runner = CreateRunner(new AnthropicApiOptions("test-api-key", "claude-sonnet-5", AgentToolsEnabled: true));

        var result = await runner.RunPromptAsync("prompt", workingDirectory.Path);

        Assert.True(result.Success);

        var secondRequestMessages = (JArray)JObject.Parse(_handler.RequestBodies[1])["messages"]!;
        var toolResult = secondRequestMessages[2]["content"]![0]!;
        Assert.True(toolResult["is_error"]!.ToObject<bool>());
        Assert.Contains("Refusing to write outside the project folder", toolResult["content"]!.ToString());
    }

    [Fact]
    public async Task RunPromptAsync_ModelKeepsRequestingToolsPastTheIterationCap_ReturnsFailure()
    {
        using var workingDirectory = new TempDirectory();

        const string ToolUseResponse = """
            data: {"type":"message_start","message":{"usage":{"input_tokens":10}}}

            data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_1","name":"glob","input":{}}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"pattern\":\"*.txt\"}"}}

            data: {"type":"content_block_stop","index":0}

            data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":5}}

            data: {"type":"message_stop"}

            """;
        _handler.ResponseBodyToReturn = ToolUseResponse;
        var runner = CreateRunner(new AnthropicApiOptions("test-api-key", "claude-sonnet-5", AgentToolsEnabled: true));

        var result = await runner.RunPromptAsync("prompt", workingDirectory.Path);

        Assert.False(result.Success);
        Assert.Contains("Stopped after", result.ErrorMessage);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("PaardeTests_").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    [Fact]
    public async Task IsAvailableAsync_ApiKeyConfigured_ReturnsTrue()
    {
        var runner = CreateRunner();

        Assert.True(await runner.IsAvailableAsync());
    }

    [Fact]
    public async Task IsAvailableAsync_ApiKeyBlank_ReturnsFalse()
    {
        var runner = CreateRunner(new AnthropicApiOptions(string.Empty, "claude-sonnet-5"));

        Assert.False(await runner.IsAvailableAsync());
    }
}
