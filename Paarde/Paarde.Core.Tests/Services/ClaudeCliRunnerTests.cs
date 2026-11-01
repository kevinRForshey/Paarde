using System.Runtime.Versioning;

using Paarde.Core.Models;
using Paarde.Core.Services;

namespace Paarde.Core.Tests.Services;

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]

// Stands a POSIX shell script in for the `claude` CLI (same approach GitStatusServiceTests takes with a
// real `git` binary) so RunPromptAsync's stream-json NDJSON parsing (T6.2.1/T6.2.3) can be exercised
// without invoking the real, paid Claude Code CLI.
[Collection("RealProcess")]
public class ClaudeCliRunnerTests : IDisposable
{
    private const string FakeClaudeScript = """
        #!/bin/sh
        cat > /dev/null
        cat <<'EOF'
        {"type":"system","subtype":"init"}
        {"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"Hello"}}}
        {"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":", world"}}}
        {"type":"stream_event","event":{"type":"content_block_stop"}}
        {"type":"result","subtype":"success","is_error":false,"duration_ms":1234,"total_cost_usd":0.0056,"usage":{"input_tokens":10,"output_tokens":5},"session_id":"session-abc"}
        EOF
        """;

    // Also echoes its argv (one per line) to stderr, so tests can assert on exactly which
    // arguments ClaudeCliRunner invoked it with (T7.3.1's --resume in particular).
    private const string ArgvEchoingFakeClaudeScript = """
        #!/bin/sh
        cat > /dev/null
        for arg in "$@"; do echo "$arg" >&2; done
        echo '{"type":"result","subtype":"success","is_error":false}'
        """;

    private readonly string _scriptPath = Path.Combine(Path.GetTempPath(), $"fake-claude-{Guid.NewGuid()}.sh");
    private readonly ClaudeCliRunner _runner;

    public ClaudeCliRunnerTests()
    {
        File.WriteAllText(_scriptPath, FakeClaudeScript);
        File.SetUnixFileMode(_scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var options = new ClaudeCliOptions(_scriptPath, string.Empty);
        _runner = new ClaudeCliRunner(() => Task.FromResult(options));
    }

    public void Dispose()
    {
        if (File.Exists(_scriptPath))
        {
            File.Delete(_scriptPath);
        }

        foreach (var path in _extraScriptPaths)
        {
            File.Delete(path);
        }
    }

    private readonly List<string> _extraScriptPaths = [];

    private ClaudeCliRunner CreateRunner(string script, bool agentToolsEnabled = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fake-claude-{Guid.NewGuid()}.sh");
        File.WriteAllText(path, script);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _extraScriptPaths.Add(path);

        return new ClaudeCliRunner(() => Task.FromResult(new ClaudeCliOptions(path, string.Empty, agentToolsEnabled)));
    }

    [Fact]
    public async Task RunPromptAsync_StreamJsonTextDeltas_AccumulatesThemAsOutput()
    {
        var result = await _runner.RunPromptAsync("prompt");

        Assert.Equal("Hello, world", result.Output);
        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RunPromptAsync_StreamJsonTextDeltas_InvokesOnTextDeltaForEachChunkInOrder()
    {
        var received = new List<string>();

        await _runner.RunPromptAsync("prompt", onTextDelta: received.Add);

        Assert.Equal(new[] { "Hello", ", world" }, received);
    }

    [Fact]
    public async Task RunPromptAsync_ResultLine_PopulatesCostAndUsageFields()
    {
        var result = await _runner.RunPromptAsync("prompt");

        Assert.Equal(0.0056, result.TotalCostUsd);
        Assert.Equal(1234, result.DurationMs);
        Assert.Equal(10, result.InputTokens);
        Assert.Equal(5, result.OutputTokens);
        Assert.Equal("session-abc", result.SessionId);
    }

    [Fact]
    public async Task RunPromptAsync_ResumeSessionIdGiven_PassesResumeFlagToTheCli()
    {
        var runner = CreateRunner(ArgvEchoingFakeClaudeScript);

        var result = await runner.RunPromptAsync("prompt", resumeSessionId: "session-xyz");

        var argv = result.ErrorMessage.Split('\n');
        Assert.Contains("--resume", argv);
        Assert.Contains("session-xyz", argv);
    }

    [Fact]
    public async Task RunPromptAsync_NoResumeSessionIdGiven_DoesNotPassResumeFlagToTheCli()
    {
        var runner = CreateRunner(ArgvEchoingFakeClaudeScript);

        var result = await runner.RunPromptAsync("prompt");

        Assert.DoesNotContain("--resume", result.ErrorMessage.Split('\n'));
    }

    [Fact]
    public async Task RunPromptAsync_AgentToolsDisabled_DoesNotPassAllowedToolsOrPermissionModeFlags()
    {
        var runner = CreateRunner(ArgvEchoingFakeClaudeScript, agentToolsEnabled: false);

        var result = await runner.RunPromptAsync("prompt");

        var argv = result.ErrorMessage.Split('\n');
        Assert.DoesNotContain("--allowedTools", argv);
        Assert.DoesNotContain("--permission-mode", argv);
    }

    [Fact]
    public async Task RunPromptAsync_AgentToolsEnabled_PassesAllowedToolsAndPermissionModeFlags()
    {
        var runner = CreateRunner(ArgvEchoingFakeClaudeScript, agentToolsEnabled: true);

        var result = await runner.RunPromptAsync("prompt");

        var argv = result.ErrorMessage.Split('\n');
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
        const string UnsupportedCliScript = """
            #!/bin/sh
            cat > /dev/null
            echo "error: unknown option '--allowedTools'" >&2
            exit 1
            """;
        var runner = CreateRunner(UnsupportedCliScript, agentToolsEnabled: true);

        var result = await runner.RunPromptAsync("prompt");

        Assert.False(result.Success);
        Assert.Contains("doesn't recognize --allowedTools", result.ErrorMessage);
    }

    [Fact]
    public async Task RunPromptAsync_AgentToolsDisabledAndCliFailsForAnUnrelatedReason_LeavesTheRawErrorMessageAlone()
    {
        const string UnsupportedCliScript = """
            #!/bin/sh
            cat > /dev/null
            echo "error: unknown option '--allowedTools'" >&2
            exit 1
            """;
        var runner = CreateRunner(UnsupportedCliScript, agentToolsEnabled: false);

        var result = await runner.RunPromptAsync("prompt");

        Assert.False(result.Success);
        Assert.DoesNotContain("doesn't recognize --allowedTools", result.ErrorMessage);
    }

    [Fact]
    public async Task RunPromptAsync_ToolUseStreamEvents_InvokeOnToolActivityWithoutPollutingAssistantText()
    {
        const string ToolUseScript = """
            #!/bin/sh
            cat > /dev/null
            cat <<'EOF'
            {"type":"system","subtype":"init"}
            {"type":"stream_event","event":{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"tool-1","name":"Read"}}}
            {"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"file_path\": \"Foo.cs\"}"}}}
            {"type":"stream_event","event":{"type":"content_block_stop","index":0}}
            {"type":"stream_event","event":{"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"Done"}}}
            {"type":"stream_event","event":{"type":"content_block_stop","index":1}}
            {"type":"result","subtype":"success","is_error":false}
            EOF
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
        Assert.True(await _runner.IsAvailableAsync());
    }

    [Fact]
    public async Task IsAvailableAsync_ExecutableMissing_ReturnsFalse()
    {
        var runner = new ClaudeCliRunner(() => Task.FromResult(new ClaudeCliOptions("/no/such/claude-binary", string.Empty)));

        Assert.False(await runner.IsAvailableAsync());
    }
}
