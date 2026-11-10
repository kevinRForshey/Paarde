using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.Models;

using Newtonsoft.Json.Linq;

namespace Paarde.Core.Services;

// The CLI implementation of ILlmRunner - a direct Anthropic API runner is meant
// to be a second one. CLI-specific configuration (executable path/arguments) is resolved through
// optionsProvider rather than taken as a per-call parameter, since Core can't depend on the
// Avalonia-layer settings service that actually stores it (see ClaudeCliOptionsProvider).
public class ClaudeCliRunner : ILlmRunner
{
    // Fixed flags required to get NDJSON stream-json output with token-level text deltas -
    // appended unconditionally after the user-configured arguments, since streaming is now
    // a hard requirement for this service to parse its own output, not an optional CLI choice.
    private static readonly string[] StreamingArguments = { "--output-format", "stream-json", "--include-partial-messages", "--verbose" };

    private readonly Func<Task<ClaudeCliOptions>> _optionsProvider;

    public ClaudeCliRunner(Func<Task<ClaudeCliOptions>>? optionsProvider = null)
    {
        _optionsProvider = optionsProvider ?? (() => Task.FromResult(ClaudeCliOptions.Default));
    }

    public async Task<bool> IsAvailableAsync()
    {
        var options = await _optionsProvider();

        return ExecutablePathResolver.ExistsOnPath(options.ExecutablePath);
    }

    public async Task<LlmRunResult> RunPromptAsync(string prompt, string? workingDirectory = null, Action<string>? onTextDelta = null, Action<string>? onToolActivity = null, string? resumeSessionId = null, CancellationToken cancellationToken = default)
    {
        var options = await _optionsProvider();

        // --resume continues the CLI's own session identified by an earlier result's
        // session_id, rather than starting a fresh conversation - this is what makes a follow-up
        // prompt carry the prior turn's context.
        var resumeArguments = string.IsNullOrWhiteSpace(resumeSessionId) ? Enumerable.Empty<string>() : new[] { "--resume", resumeSessionId };

        // "default" (not "bypassPermissions") because the CLI itself should still
        // own enforcing the allow-list - Paarde isn't re-implementing permission checking here, just
        // asking the CLI to let its own agent loop read/search the repo.
        var agentToolsArguments = options.AgentToolsEnabled
            ? new[] { "--allowedTools", "Read", "Grep", "Glob", "--permission-mode", "default" }
            : Enumerable.Empty<string>();

        var arguments = options.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).Concat(resumeArguments).Concat(agentToolsArguments).Concat(StreamingArguments);

        // On Windows this is routed through cmd.exe so PATH resolution picks up the claude.cmd shim installed
        // by npm; other platforms can invoke the executable directly. The prompt is written to stdin rather
        // than passed as an argument: command-line parsers break on embedded newlines, which was
        // truncating/emptying multi-line prompts.
        var startInfo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ProcessStartInfo
            {
                FileName = "cmd.exe",
                ArgumentList = { "/c", options.ExecutablePath },
            }
            : new ProcessStartInfo
            {
                FileName = options.ExecutablePath,
            };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardInputEncoding = new UTF8Encoding(false);
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        using var process = new Process { StartInfo = startInfo };

        var assistantText = new StringBuilder();
        var standardError = new StringBuilder();

        // A tool_use content block's name arrives on content_block_start, but
        // its input only finishes streaming in (via input_json_delta) by content_block_stop - so
        // these track in-progress blocks by index the same way AnthropicApiRunner.ParseStreamAsync
        // does, keyed only for blocks that are actually tool_use (text blocks are still just
        // appended to assistantText directly, unchanged from before this).
        var toolNamesByIndex = new Dictionary<int, string>();
        var toolInputJsonByIndex = new Dictionary<int, StringBuilder>();
        double? totalCostUsd = null;
        long? durationMs = null;
        int? inputTokens = null;
        int? outputTokens = null;
        string? sessionId = null;

        // Each line of stdout is one NDJSON object rather than plain text now that the CLI
        // is invoked with --output-format stream-json. A line that isn't a well-formed JSON object
        // is swallowed rather than crashing the run - the CLI shouldn't produce one, but this is an
        // external process's output, not something this code controls.
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            JObject line;

            try
            {
                line = JObject.Parse(e.Data);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return;
            }

            switch (line["type"]?.ToString())
            {
                case "stream_event":
                    var streamEvent = line["event"] as JObject;

                    switch (streamEvent?["type"]?.ToString())
                    {
                        case "content_block_start":
                            if (streamEvent["content_block"] is JObject { } startBlock && startBlock["type"]?.ToString() == "tool_use")
                            {
                                var startIndex = streamEvent["index"]?.ToObject<int>() ?? 0;
                                toolNamesByIndex[startIndex] = startBlock["name"]?.ToString() ?? "tool";
                                toolInputJsonByIndex[startIndex] = new StringBuilder();
                            }

                            break;

                        case "content_block_delta":
                            if (streamEvent["delta"] is JObject delta)
                            {
                                if (delta["type"]?.ToString() == "text_delta")
                                {
                                    var text = delta["text"]?.ToString() ?? string.Empty;
                                    assistantText.Append(text);
                                    onTextDelta?.Invoke(text);
                                }
                                else if (delta["type"]?.ToString() == "input_json_delta")
                                {
                                    var deltaIndex = streamEvent["index"]?.ToObject<int>() ?? 0;

                                    if (toolInputJsonByIndex.TryGetValue(deltaIndex, out var builder))
                                    {
                                        builder.Append(delta["partial_json"]?.ToString() ?? string.Empty);
                                    }
                                }
                            }

                            break;

                        case "content_block_stop":
                            var stopIndex = streamEvent["index"]?.ToObject<int>() ?? 0;

                            if (toolNamesByIndex.TryGetValue(stopIndex, out var toolName))
                            {
                                var json = toolInputJsonByIndex[stopIndex].ToString();
                                JObject? toolInput = null;

                                try
                                {
                                    toolInput = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
                                }
                                catch (Newtonsoft.Json.JsonException)
                                {
                                }

                                onToolActivity?.Invoke(ToolActivityDescriber.Describe(toolName, toolInput));
                            }

                            break;
                    }

                    break;

                case "result":
                    totalCostUsd = line["total_cost_usd"]?.ToObject<double?>();
                    durationMs = line["duration_ms"]?.ToObject<long?>();
                    inputTokens = (line["usage"] as JObject)?["input_tokens"]?.ToObject<int?>();
                    outputTokens = (line["usage"] as JObject)?["output_tokens"]?.ToObject<int?>();
                    sessionId = line["session_id"]?.ToString();
                    break;
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                standardError.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // WaitForExitAsync throwing doesn't stop the child process on its own - without
            // this, cancelling (or a timeout firing) would leave `claude` - and, on Windows, the
            // cmd.exe wrapper's own child - running in the background after this call returns.
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        var errorMessage = standardError.ToString().TrimEnd();

        // A CLI build too old to recognize --allowedTools/--permission-mode fails with its
        // own argument-parsing error (e.g. "error: unknown option '--allowedTools'") rather than
        // anything that names the actual problem - only worth checking for when this run is the one
        // that added those flags, since an unrelated argument-parsing failure shouldn't be relabeled.
        if (process.ExitCode != 0 && options.AgentToolsEnabled && LooksLikeUnrecognizedAgentToolsFlag(errorMessage))
        {
            errorMessage = "This Claude Code CLI build doesn't recognize --allowedTools/--permission-mode, used because \"Enable read-only agent tools\" is turned on in Settings. Upgrade the CLI (npm install -g @anthropic-ai/claude-code) or turn that setting off.";
        }

        return new LlmRunResult
        {
            Success = process.ExitCode == 0,
            Output = assistantText.ToString().TrimEnd(),
            ErrorMessage = errorMessage,
            ExitCode = process.ExitCode,
            TotalCostUsd = totalCostUsd,
            DurationMs = durationMs,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            SessionId = sessionId,
        };
    }

    private static bool LooksLikeUnrecognizedAgentToolsFlag(string stderr) =>
        stderr.Contains("unknown option", StringComparison.OrdinalIgnoreCase) &&
        (stderr.Contains("--allowedTools", StringComparison.OrdinalIgnoreCase) || stderr.Contains("--permission-mode", StringComparison.OrdinalIgnoreCase));
}
