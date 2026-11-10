using Paarde.Core.Models;
using Paarde.Core.Services;
using Paarde.Core.Tests.TestSupport;

namespace Paarde.Core.Tests.Services;

// Stands a fake script in for the `claude` CLI (same approach GitStatusServiceTests takes with a
// real `git` binary) so RunPromptAsync's stream-json NDJSON parsing can be exercised
// without invoking the real, paid Claude Code CLI. Python rather than sh so it runs on Windows too
// (FakeExecutable); tests that need it return early where Python isn't installed.
[Collection("RealProcess")]
public class ClaudeCliRunnerTests : IDisposable
{
    private const string FakeClaudeScript = """
        import sys
        sys.stdin.read()
        print('{"type":"system","subtype":"init"}')
        print('{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"Hello"}}}')
        print('{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":", world"}}}')
        print('{"type":"stream_event","event":{"type":"content_block_stop"}}')
        print('{"type":"result","subtype":"success","is_error":false,"duration_ms":1234,"total_cost_usd":0.0056,"usage":{"input_tokens":10,"output_tokens":5},"session_id":"session-abc"}')
        """;

    // Also echoes its argv (one per line) to stderr, so tests can assert on exactly which
    // arguments ClaudeCliRunner invoked it with (T7.3.1's --resume in particular).
    private const string ArgvEchoingFakeClaudeScript = """
        import sys
        sys.stdin.read()
        for arg in sys.argv[1:]:
            print(arg, file=sys.stderr)
        print('{"type":"result","subtype":"success","is_error":false}')
        """;

    private const string UnsupportedCliScript = """
        import sys
        sys.stdin.read()
        print("error: unknown option '--allowedTools'", file=sys.stderr)
        sys.exit(1)
        """;

    private readonly List<FakeExecutable> _fakes = [];

    public void Dispose()
    {
        foreach (var fake in _fakes)
        {
            fake.Dispose();
        }
    }

    private ClaudeCliRunner CreateRunner(string script, bool agentToolsEnabled = false)
    {
        var fake = new FakeExecutable(script);
        _fakes.Add(fake);

        return new ClaudeCliRunner(() => Task.FromResult(new ClaudeCliOptions(fake.ExecutablePath, string.Empty, agentToolsEnabled)));
    }

    // Each argument stderr line; the Windows .cmd wrapper's stderr lines end in \r\n.
    private static string[] ReadArgv(LlmRunResult result) => result.ErrorMessage.ReplaceLineEndings("\n").Split('\n');

    [Fact]
    public async Task RunPromptAsync_StreamJsonTextDeltas_AccumulatesThemAsOutput()
    {
        if (!FakeExecutable.IsPythonAvailable)
        {
            return;
        }

        var result = await CreateRunner(FakeClaudeScript).RunPromptAsync("prompt");

        Assert.Equal("Hello, world", result.Output);
        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RunPromptAsync_StreamJsonTextDeltas_InvokesOnTextDeltaForEachChunkInOrder()
    {
        if (!FakeExecutable.IsPythonAvailable)
        {
            return;
        }

        var received = new List<string>();

        await CreateRunner(FakeClaudeScript).RunPromptAsync("prompt", onTextDelta: received.Add);

        Assert.Equal(new[] { "Hello", ", world" }, received);
    }

    [Fact]
    public async Task RunPromptAsync_ResultLine_PopulatesCostAndUsageFields()
    {
        if (!FakeExecutable.IsPythonAvailable)
        {
            return;
        }

        var result = await CreateRunner(FakeClaudeScript).RunPromptAsync("prompt");

        Assert.Equal(0.0056, result.TotalCostUsd);
        Assert.Equal(1234, result.DurationMs);
        Assert.Equal(10, result.InputTokens);
        Assert.Equal(5, result.OutputTokens);
        Assert.Equal("session-abc", result.SessionId);
    }

    [Fact]
    public async Task RunPromptAsync_ResumeSessionIdGiven_PassesResumeFlagToTheCli()
    {
        if (!FakeExecutable.IsPythonAvailable)
        {
            return;
        }

        var runner = CreateRunner(ArgvEchoingFakeClaudeScript);

        var result = await runner.RunPromptAsync("prompt", resumeSessionId: "session-xyz");

        var argv = ReadArgv(result);
        Assert.Contains("--resume", argv);
        Assert.Contains("session-xyz", argv);
    }

    [Fact]
    public async Task RunPromptAsync_NoResumeSessionIdGiven_DoesNotPassResumeFlagToTheCli()
    {
        if (!FakeExecutable.IsPythonAvailable)
        {
            return;
        }

        var runner = CreateRunner(ArgvEchoingFakeClaudeScript);

        var result = await runner.RunPromptAsync("prompt");

        Assert.DoesNotContain("--resume", ReadArgv(result));
    }

    [Fact]
    public async Task RunPromptAsync_AgentToolsDisabled_DoesNotPassAllowedToolsOrPermissionModeFlags()
    {
        if (!FakeExecutable.IsPythonAvailable)
        {
            return;
        }

        var runner = CreateRunner(ArgvEchoingFakeClaudeScript, agentToolsEnabled: false);

        var result = await runner.RunPromptAsync("prompt");

        var argv = ReadArgv(result);
        Assert.DoesNotContain("--allowedTools", argv);
        Assert.DoesNotContain("--permission-mode", argv);
    }

    [Fact]
    public async Task RunPromptAsync_AgentToolsEnabled_PassesAllowedToolsAndPermissionModeFlags()
    {
        if (!FakeExecutable.IsPythonAvailable)
        {
            return;
        }

        var runner = CreateRunner(ArgvEchoingFakeClaudeScript, agentToolsEnabled: true);

        var result = await runner.RunPromptAsync("prompt");

        var argv = ReadArgv(result);
        Assert.Contains("--allowedTools", argv);
        Assert.Contains("Read", argv);
        Assert.Contains("Grep", argv);
        Assert.Contains("Glob", argv);
        Assert.Contains("--permission-mode", argv);
        Assert.Contains("default", argv);
    }

    [Fact]
    public async Task RunPromptAsync_AgentToolsEnabledAndCliRejectsTheFlag_ReturnsAnActionableErrorMessage()
    {
        if (!FakeExecutable.IsPythonAvailable)
        {
            return;
        }

        var runner = CreateRunner(UnsupportedCliScript, agentToolsEnabled: true);

        var result = await runner.RunPromptAsync("prompt");

        Assert.False(result.Success);
        Assert.Contains("doesn't recognize --allowedTools", result.ErrorMessage);
    }

    [Fact]
    public async Task RunPromptAsync_AgentToolsDisabledAndCliFailsForAnUnrelatedReason_LeavesTheRawErrorMessageAlone()
    {
        if (!FakeExecutable.IsPythonAvailable)
        {
            return;
        }

        var runner = CreateRunner(UnsupportedCliScript, agentToolsEnabled: false);

        var result = await runner.RunPromptAsync("prompt");

        Assert.False(result.Success);
        Assert.DoesNotContain("doesn't recognize --allowedTools", result.ErrorMessage);
    }

    [Fact]
    public async Task RunPromptAsync_ToolUseStreamEvents_InvokeOnToolActivityWithoutPollutingAssistantText()
    {
        if (!FakeExecutable.IsPythonAvailable)
        {
            return;
        }

        const string ToolUseScript = """
            import sys
            sys.stdin.read()
            print('{"type":"system","subtype":"init"}')
            print('{"type":"stream_event","event":{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"tool-1","name":"Read"}}}')
            print('{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\\"file_path\\": \\"Foo.cs\\"}"}}}')
            print('{"type":"stream_event","event":{"type":"content_block_stop","index":0}}')
            print('{"type":"stream_event","event":{"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"Done"}}}')
            print('{"type":"stream_event","event":{"type":"content_block_stop","index":1}}')
            print('{"type":"result","subtype":"success","is_error":false}')
            """;
        var runner = CreateRunner(ToolUseScript);
        var toolActivities = new List<string>();

        var result = await runner.RunPromptAsync("prompt", onToolActivity: toolActivities.Add);

        Assert.Equal(new[] { "Reading `Foo.cs`" }, toolActivities);
        Assert.Equal("Done", result.Output);
    }

    [Fact]
    public async Task IsAvailableAsync_ScriptExists_ReturnsTrue()
    {
        var runner = CreateRunner(FakeClaudeScript);

        Assert.True(await runner.IsAvailableAsync());
    }

    [Fact]
    public async Task IsAvailableAsync_ExecutableMissing_ReturnsFalse()
    {
        var runner = new ClaudeCliRunner(() => Task.FromResult(new ClaudeCliOptions("/no/such/claude-binary", string.Empty)));

        Assert.False(await runner.IsAvailableAsync());
    }
}
