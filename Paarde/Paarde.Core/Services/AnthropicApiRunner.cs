using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.Models;

using Newtonsoft.Json.Linq;

namespace Paarde.Core.Services;

// The direct-API implementation of ILlmRunner - a second implementation alongside
// ClaudeCliRunner, behind the same interface per T6.3.1's design. Unlike the CLI, the raw Messages
// API is stateless (no --resume equivalent): this runner owns conversation history itself, keyed by
// the SessionId it hands back, so resumeSessionId can still work the same way callers already rely
// on for ClaudeCliRunner. That history only lives in memory for this runner instance's lifetime
// (the app's lifetime, since it's registered as a singleton) - it doesn't persist across restarts
// the way the CLI's own session storage does.
public class AnthropicApiRunner : ILlmRunner
{
    private const string MessagesEndpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxTokens = 8192;

    // A bound on how many tool-use round trips a single RunPromptAsync call will
    // make before giving up - without one, a model stuck repeatedly requesting tools (its own bug,
    // or a tool result it keeps misreading) would hang this call and keep billing API calls
    // indefinitely instead of surfacing as a normal failure.
    private const int MaxAgentToolIterations = 25;

    private const int MaxGrepResults = 200;
    private const int MaxGlobResults = 200;

    // Read-only tool schemas (Read/Grep/Glob - Write/Edit/Bash are out of scope per Epic 11's
    // explicit non-goal) offered to the model via the Messages API's native `tools` parameter when
    // AnthropicApiOptions.AgentToolsEnabled is on.
    private static readonly JArray ReadOnlyToolDefinitions = new(
        new JObject
        {
            ["name"] = "read_file",
            ["description"] = "Read the full text contents of a file in the project, given a path relative to the project root.",
            ["input_schema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["path"] = new JObject { ["type"] = "string", ["description"] = "File path relative to the project root." },
                },
                ["required"] = new JArray("path"),
            },
        },
        new JObject
        {
            ["name"] = "grep",
            ["description"] = "Search for a regular expression pattern across files in the project (or a file/directory within it) and return matching lines with their file path and line number.",
            ["input_schema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["pattern"] = new JObject { ["type"] = "string", ["description"] = "A .NET regular expression." },
                    ["path"] = new JObject { ["type"] = "string", ["description"] = "Optional file or directory to scope the search to, relative to the project root. Defaults to the whole project." },
                },
                ["required"] = new JArray("pattern"),
            },
        },
        new JObject
        {
            ["name"] = "glob",
            ["description"] = "List files in the project whose path matches a glob pattern, e.g. **/*.cs or src/*.ts.",
            ["input_schema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["pattern"] = new JObject { ["type"] = "string" },
                },
                ["required"] = new JArray("pattern"),
            },
        });

    private static readonly HttpClient SharedHttpClient = new();

    private readonly Func<Task<AnthropicApiOptions>> _optionsProvider;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, List<JObject>> _conversations = new();

    public AnthropicApiRunner(Func<Task<AnthropicApiOptions>>? optionsProvider = null, HttpClient? httpClient = null)
    {
        _optionsProvider = optionsProvider ?? (() => Task.FromResult(AnthropicApiOptions.Default));
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public async Task<bool> IsAvailableAsync()
    {
        var options = await _optionsProvider();

        return !string.IsNullOrWhiteSpace(options.ApiKey);
    }

    public async Task<LlmRunResult> RunPromptAsync(string prompt, string? workingDirectory = null, Action<string>? onTextDelta = null, Action<string>? onToolActivity = null, string? resumeSessionId = null, CancellationToken cancellationToken = default)
    {
        var options = await _optionsProvider();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return new LlmRunResult { Success = false, ErrorMessage = "No Anthropic API key configured. Add one on the Settings page." };
        }

        // T7.3.1 parity: continues the same back-and-forth resumeSessionId identifies (if any history
        // is still held for it) by sending the whole prior transcript back, since the API itself has
        // no server-side session concept to resume like the CLI does.
        var history = resumeSessionId is not null && _conversations.TryGetValue(resumeSessionId, out var existing)
            ? new List<JObject>(existing)
            : new List<JObject>();

        history.Add(new JObject { ["role"] = "user", ["content"] = prompt });

        var stopwatch = Stopwatch.StartNew();
        int? inputTokens = null;
        int? outputTokens = null;
        string? stopReason = null;
        var finalText = string.Empty;
        var reachedIterationLimit = false;

        // A single call used to mean a single request/response. With tools
        // enabled, the model can come back asking to run one or more of them before it's ready to
        // answer - each iteration is one request/response turn; the loop ends as soon as a turn's
        // stop_reason isn't "tool_use" (or the MaxAgentToolIterations safety cap trips). With no
        // tools offered (AgentToolsEnabled off, the default), stop_reason can never be "tool_use",
        // so this always runs exactly once - identical to the prior single-shot behavior.
        for (var iteration = 0; iteration < MaxAgentToolIterations; iteration++)
        {
            var requestBody = new JObject
            {
                ["model"] = options.Model,
                ["max_tokens"] = MaxTokens,
                ["stream"] = true,
                ["messages"] = new JArray(history),
            };

            if (options.AgentToolsEnabled)
            {
                requestBody["tools"] = ReadOnlyToolDefinitions;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, MessagesEndpoint)
            {
                Content = new StringContent(requestBody.ToString(), Encoding.UTF8, "application/json"),
            };

            request.Headers.Add("x-api-key", options.ApiKey);
            request.Headers.Add("anthropic-version", AnthropicVersion);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return new LlmRunResult { Success = false, ErrorMessage = ExtractErrorMessage(errorBody, response.StatusCode.ToString()) };
            }

            var parsed = await ParseStreamAsync(response, onTextDelta, onToolActivity, cancellationToken);
            inputTokens ??= parsed.InputTokens;
            outputTokens = (outputTokens ?? 0) + (parsed.OutputTokens ?? 0);
            stopReason = parsed.StopReason;
            finalText = parsed.Text;

            history.Add(new JObject { ["role"] = "assistant", ["content"] = new JArray(parsed.ContentBlocks) });

            if (stopReason != "tool_use")
            {
                break;
            }

            var toolUseBlocks = parsed.ContentBlocks.Where(block => block["type"]?.ToString() == "tool_use").ToList();

            if (toolUseBlocks.Count == 0)
            {
                // stop_reason said tool_use but nothing actually parsed as one - treat as done
                // rather than looping forever on a turn with no tool call to execute.
                break;
            }

            var toolResults = new JArray();

            foreach (var toolUse in toolUseBlocks)
            {
                var (toolOutput, isError) = ExecuteReadOnlyTool(toolUse["name"]!.ToString(), toolUse["input"] as JObject ?? new JObject(), workingDirectory);

                toolResults.Add(new JObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = toolUse["id"]!.ToString(),
                    ["content"] = toolOutput,
                    ["is_error"] = isError,
                });
            }

            history.Add(new JObject { ["role"] = "user", ["content"] = toolResults });

            if (iteration == MaxAgentToolIterations - 1)
            {
                reachedIterationLimit = true;
            }
        }

        stopwatch.Stop();

        if (reachedIterationLimit)
        {
            return new LlmRunResult { Success = false, Output = finalText, ErrorMessage = $"Stopped after {MaxAgentToolIterations} tool calls without the model finishing. Try a narrower prompt." };
        }

        // refusal/other isn't modeled as a failure here - the same way the CLI runner only fails on a
        // non-zero exit code, not on the content of what came back - but max_tokens getting hit before
        // the model finished is worth surfacing distinctly, since the caller's JSON parse is about to
        // fail on truncated output and "the API call itself failed" would be the wrong explanation.
        if (stopReason == "max_tokens")
        {
            return new LlmRunResult { Success = false, Output = finalText, ErrorMessage = $"The response was cut off at the {MaxTokens}-token limit before finishing." };
        }

        var sessionId = resumeSessionId is not null && _conversations.ContainsKey(resumeSessionId) ? resumeSessionId : Guid.NewGuid().ToString();
        _conversations[sessionId] = history;

        return new LlmRunResult
        {
            Success = true,
            Output = finalText,
            DurationMs = stopwatch.ElapsedMilliseconds,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            SessionId = sessionId,

            // Unlike the CLI's "result" line, the Messages API doesn't return a total_cost_usd - doing
            // that here would mean hand-maintaining a per-model price table that silently goes stale
            // whenever Anthropic's pricing changes, so this is left null rather than guessed at.
            TotalCostUsd = null,
        };
    }

    private readonly record struct StreamParseResult(List<JObject> ContentBlocks, string Text, int? InputTokens, int? OutputTokens, string? StopReason);

    // One turn's worth of SSE parsing - message_start/message_delta exactly as before, plus
    // content_block_start/delta/stop now generalized to build up either a text block or a tool_use
    // block per index (previously only text_delta was handled; everything else was silently
    // dropped, which is fine for a turn with no tools offered but would lose tool_use entirely).
    private static async Task<StreamParseResult> ParseStreamAsync(HttpResponseMessage response, Action<string>? onTextDelta, Action<string>? onToolActivity, CancellationToken cancellationToken)
    {
        var blocks = new SortedDictionary<int, JObject>();
        var textByIndex = new Dictionary<int, StringBuilder>();
        var toolInputJsonByIndex = new Dictionary<int, StringBuilder>();
        int? inputTokens = null;
        int? outputTokens = null;
        string? stopReason = null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            JObject payload;

            try
            {
                payload = JObject.Parse(line["data: ".Length..]);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                continue;
            }

            switch (payload["type"]?.ToString())
            {
                case "message_start":
                    inputTokens = (payload["message"]?["usage"] as JObject)?["input_tokens"]?.ToObject<int?>();
                    break;

                case "content_block_start":
                    var startIndex = payload["index"]!.ToObject<int>();
                    var contentBlock = payload["content_block"] as JObject;

                    if (contentBlock?["type"]?.ToString() == "tool_use")
                    {
                        blocks[startIndex] = new JObject { ["type"] = "tool_use", ["id"] = contentBlock["id"], ["name"] = contentBlock["name"] };
                        toolInputJsonByIndex[startIndex] = new StringBuilder();
                    }
                    else
                    {
                        blocks[startIndex] = new JObject { ["type"] = "text", ["text"] = string.Empty };
                        textByIndex[startIndex] = new StringBuilder();
                    }

                    break;

                case "content_block_delta":
                    var deltaIndex = payload["index"]!.ToObject<int>();

                    if (payload["delta"] is JObject { } delta)
                    {
                        if (delta["type"]?.ToString() == "text_delta")
                        {
                            var text = delta["text"]?.ToString() ?? string.Empty;
                            textByIndex[deltaIndex].Append(text);
                            onTextDelta?.Invoke(text);
                        }
                        else if (delta["type"]?.ToString() == "input_json_delta")
                        {
                            toolInputJsonByIndex[deltaIndex].Append(delta["partial_json"]?.ToString() ?? string.Empty);
                        }
                    }

                    break;

                case "content_block_stop":
                    var stopIndex = payload["index"]!.ToObject<int>();

                    if (blocks[stopIndex]["type"]!.ToString() == "text")
                    {
                        blocks[stopIndex]["text"] = textByIndex[stopIndex].ToString();
                    }
                    else
                    {
                        var json = toolInputJsonByIndex[stopIndex].ToString();
                        var toolInput = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
                        blocks[stopIndex]["input"] = toolInput;

                        // Fires once the tool's full input has actually finished
                        // streaming in, not at content_block_start, since e.g. a read_file's path
                        // isn't known yet at that point.
                        onToolActivity?.Invoke(ToolActivityDescriber.Describe(blocks[stopIndex]["name"]!.ToString(), toolInput));
                    }

                    break;

                case "message_delta":
                    outputTokens = (payload["usage"] as JObject)?["output_tokens"]?.ToObject<int?>() ?? outputTokens;
                    stopReason = payload["delta"]?["stop_reason"]?.ToString() ?? stopReason;
                    break;
            }
        }

        var orderedBlocks = blocks.Select(pair => pair.Value).ToList();
        var combinedText = string.Concat(orderedBlocks.Where(block => block["type"]!.ToString() == "text").Select(block => block["text"]!.ToString()));

        return new StreamParseResult(orderedBlocks, combinedText, inputTokens, outputTokens, stopReason);
    }

    // Dispatches one tool_use block to the matching read-only implementation,
    // scoped to workingDirectory through the same FilePathContainment guard writes already go
    // through - a model-requested path that escapes the project comes back as a rejected
    // tool call (is_error, with the guard's own message) rather than an unhandled exception that
    // would kill the whole run over one bad tool call.
    private static (string Output, bool IsError) ExecuteReadOnlyTool(string toolName, JObject input, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return ("No project folder is selected, so there's nothing to read.", true);
        }

        try
        {
            return toolName switch
            {
                "read_file" => (ReadFile(workingDirectory, input["path"]?.ToString() ?? string.Empty), false),
                "grep" => (Grep(workingDirectory, input["pattern"]?.ToString() ?? string.Empty, input["path"]?.ToString()), false),
                "glob" => (Glob(workingDirectory, input["pattern"]?.ToString() ?? string.Empty), false),
                _ => ($"Unknown tool '{toolName}'.", true),
            };
        }
        catch (InvalidOperationException ex)
        {
            return (ex.Message, true);
        }
    }

    private static string ReadFile(string workingDirectory, string path)
    {
        var fullPath = FilePathContainment.Resolve(workingDirectory, path);

        if (!File.Exists(fullPath))
        {
            return $"'{path}' doesn't exist.";
        }

        try
        {
            return File.ReadAllText(fullPath);
        }
        catch (IOException ex)
        {
            return $"Failed to read '{path}': {ex.Message}";
        }
    }

    private static string Grep(string workingDirectory, string pattern, string? path)
    {
        var searchRoot = FilePathContainment.Resolve(workingDirectory, path ?? string.Empty);

        if (!Directory.Exists(searchRoot) && !File.Exists(searchRoot))
        {
            return $"'{path}' doesn't exist.";
        }

        Regex regex;

        try
        {
            regex = new Regex(pattern);
        }
        catch (ArgumentException ex)
        {
            return $"'{pattern}' isn't a valid regular expression: {ex.Message}";
        }

        var files = File.Exists(searchRoot) ? new[] { searchRoot } : Directory.EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories);
        var matches = new List<string>();

        foreach (var file in files)
        {
            if (matches.Count >= MaxGrepResults)
            {
                break;
            }

            string[] lines;

            try
            {
                lines = File.ReadAllLines(file);
            }
            catch (IOException)
            {
                continue;
            }

            for (var lineNumber = 0; lineNumber < lines.Length && matches.Count < MaxGrepResults; lineNumber++)
            {
                if (regex.IsMatch(lines[lineNumber]))
                {
                    var relativePath = Path.GetRelativePath(workingDirectory, file).Replace('\\', '/');
                    matches.Add($"{relativePath}:{lineNumber + 1}:{lines[lineNumber].Trim()}");
                }
            }
        }

        return matches.Count == 0 ? "No matches found." : string.Join('\n', matches);
    }

    // Supports the common agent-emitted shapes ("**/*.cs", "src/*.ts", "*.md") rather than a full
    // glob grammar: a "**" segment is treated as "search all subdirectories" (which
    // SearchOption.AllDirectories already does), and any other leading directory segments scope the
    // search root. Good enough for "find files like this" without hand-rolling a real glob matcher
    // or pulling in a dedicated package for three read-only tools.
    private static string Glob(string workingDirectory, string pattern)
    {
        var segments = pattern.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return "No files matched.";
        }

        var filePattern = segments[^1];
        var directorySegments = segments[..^1].Where(segment => segment != "**");
        var searchRoot = FilePathContainment.Resolve(workingDirectory, string.Join('/', directorySegments));

        if (!Directory.Exists(searchRoot))
        {
            return "No files matched.";
        }

        var matches = Directory.GetFiles(searchRoot, filePattern, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(workingDirectory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(MaxGlobResults)
            .ToList();

        return matches.Count == 0 ? "No files matched." : string.Join('\n', matches);
    }

    private static string ExtractErrorMessage(string responseBody, string fallback)
    {
        try
        {
            var error = JObject.Parse(responseBody);
            return error["error"]?["message"]?.ToString() ?? fallback;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return fallback;
        }
    }
}
