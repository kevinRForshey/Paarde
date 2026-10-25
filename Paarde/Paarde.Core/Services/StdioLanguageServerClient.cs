using System.Diagnostics;
using System.Text;

using Newtonsoft.Json.Linq;

using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.Models;

namespace Paarde.Core.Services;

// LSP transport is JSON-RPC 2.0 framed with an HTTP-style "Content-Length: N\r\n\r\n" header
// (https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#baseProtocol)
// over the server process's stdio - nothing like ClaudeCliRunner/ProcessCommandRunner's
// line-delimited NDJSON/plain-text stdout, so this reads raw bytes itself rather than
// Process.OutputDataReceived's line-based callback.
public class StdioLanguageServerClient : ILanguageServerClient, IAsyncDisposable
{
    private readonly Func<Task<LanguageServerOptions>> _optionsProvider;
    private readonly object _lifecycleLock = new();

    private Process? _process;
    private CancellationTokenSource? _readLoopCancellationTokenSource;
    private Task? _readLoopTask;
    private Task? _stderrDrainTask;
    private string? _workingDirectoryPath;
    private int _nextRequestId;
    private readonly Dictionary<int, TaskCompletionSource<JToken?>> _pendingRequests = new();
    private readonly Dictionary<string, int> _documentVersionsByUri = new();

    public StdioLanguageServerClient(Func<Task<LanguageServerOptions>>? optionsProvider = null)
    {
        _optionsProvider = optionsProvider ?? (() => Task.FromResult(LanguageServerOptions.Defaults[0]));
    }

    public bool IsRunning => _process is { HasExited: false };

    public event Action<string, IReadOnlyList<LspDiagnostic>>? DiagnosticsPublished;

    public async Task<bool> IsAvailableAsync()
    {
        var options = await _optionsProvider();

        return ExecutablePathResolver.ExistsOnPath(options.ExecutablePath);
    }

    public async Task StartAsync(string workingDirectoryPath, CancellationToken cancellationToken = default)
    {
        if (IsRunning && string.Equals(_workingDirectoryPath, workingDirectoryPath, StringComparison.Ordinal))
        {
            return;
        }

        await StopAsync();

        var options = await _optionsProvider();
        var startInfo = BuildStartInfo(options, workingDirectoryPath);

        var process = new Process { StartInfo = startInfo };
        process.Start();

        _process = process;
        _workingDirectoryPath = workingDirectoryPath;
        _documentVersionsByUri.Clear();

        var readLoopCancellationTokenSource = new CancellationTokenSource();
        _readLoopCancellationTokenSource = readLoopCancellationTokenSource;
        _readLoopTask = Task.Run(() => ReadLoopAsync(process.StandardOutput.BaseStream, readLoopCancellationTokenSource.Token));

        // RedirectStandardError fills the OS pipe buffer (~64KB) if nothing ever reads it, which
        // blocks the child process's writes - including mid-handshake, before "initialize" ever
        // gets a response. Drained and discarded rather than surfaced; stderr content from language
        // servers is diagnostic noise, not something this client acts on.
        _stderrDrainTask = Task.Run(() => DrainStandardErrorAsync(process.StandardError.BaseStream, readLoopCancellationTokenSource.Token));

        var rootUri = PathToUri(workingDirectoryPath);
        await SendRequestAsync("initialize", new JObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = rootUri,

            // specs/multi-language-support/requirements.md R5: previously an empty object - this
            // app always relied on publishDiagnostics/definition/references without ever telling a
            // server it supports them. Structured so adding "completion" later
            // (specs/editor-autocomplete) is a one-property addition, not a restructure.
            ["capabilities"] = new JObject
            {
                ["textDocument"] = new JObject
                {
                    ["publishDiagnostics"] = new JObject(),
                    ["definition"] = new JObject(),
                    ["references"] = new JObject(),

                    // specs/editor-autocomplete/requirements.md R2.1: snippetSupport is false,
                    // matching this spec's non-goal of snippet-placeholder expansion.
                    ["completion"] = new JObject
                    {
                        ["completionItem"] = new JObject { ["snippetSupport"] = false },
                    },
                },
            },
        }, cancellationToken);

        await SendNotificationAsync("initialized", new JObject());
    }

    // Extracted from StartAsync as a pure function (no process spawned, no I/O beyond the PATH
    // probe inside ExecutablePathResolver) so the Windows shell-wrapper decision below is directly
    // unit-testable without standing up a real server process. Internal + InternalsVisibleTo
    // (Paarde.Core.Tests) rather than public - this is an implementation detail of StartAsync, not
    // part of ILanguageServerClient's surface.
    internal static ProcessStartInfo BuildStartInfo(LanguageServerOptions options, string workingDirectoryPath)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectoryPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Epic 18 follow-up: on Windows, npm-installed servers (typescript-language-server,
        // pyright-langserver's own nodeenv-based install path, etc.) resolve on PATH as a ".cmd"
        // shim, not a native .exe - Process.Start(UseShellExecute: false) can launch a .exe
        // directly via CreateProcess, but a .cmd/.bat only runs through cmd.exe's own batch
        // interpreter. ExistsOnPath (IsAvailableAsync's preflight) already matches ".cmd", so
        // launching has to resolve the same way rather than trusting options.ExecutablePath as a
        // literal, directly-runnable FileName.
        var resolvedPath = ExecutablePathResolver.ResolveFullPath(options.ExecutablePath) ?? options.ExecutablePath;

        if (ExecutablePathResolver.RequiresShellWrapper(resolvedPath))
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(resolvedPath);
        }
        else
        {
            startInfo.FileName = resolvedPath;
        }

        foreach (var argument in options.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public async Task StopAsync()
    {
        Process? process;

        lock (_lifecycleLock)
        {
            process = _process;
            _process = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                // Best-effort graceful shutdown handshake (LSP's documented sequence) - a server
                // that's gone unresponsive shouldn't block the kill below, so this is bounded.
                using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await SendRequestAsync("shutdown", null, shutdownTimeout.Token);
                await SendNotificationAsync("exit", null);
            }
        }
        catch
        {
            // Shutdown handshake is advisory - the Kill below is what actually guarantees the
            // process stops, regardless of why the handshake didn't complete.
        }

        _readLoopCancellationTokenSource?.Cancel();

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        try
        {
            await (_readLoopTask ?? Task.CompletedTask);
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            await (_stderrDrainTask ?? Task.CompletedTask);
        }
        catch (OperationCanceledException)
        {
        }

        FailAllPendingRequests();
        process.Dispose();
        _readLoopCancellationTokenSource?.Dispose();
        _readLoopCancellationTokenSource = null;
        _readLoopTask = null;
        _stderrDrainTask = null;
    }

    public Task NotifyDocumentOpenedAsync(string filePath, string text, string languageId)
    {
        var uri = PathToUri(filePath);
        _documentVersionsByUri[uri] = 1;

        return SendNotificationAsync("textDocument/didOpen", new JObject
        {
            ["textDocument"] = new JObject
            {
                ["uri"] = uri,
                ["languageId"] = languageId,
                ["version"] = 1,
                ["text"] = text,
            },
        });
    }

    public Task NotifyDocumentChangedAsync(string filePath, string text)
    {
        var uri = PathToUri(filePath);
        var version = _documentVersionsByUri[uri] = _documentVersionsByUri.GetValueOrDefault(uri, 1) + 1;

        return SendNotificationAsync("textDocument/didChange", new JObject
        {
            ["textDocument"] = new JObject { ["uri"] = uri, ["version"] = version },
            ["contentChanges"] = new JArray(new JObject { ["text"] = text }),
        });
    }

    public Task NotifyDocumentClosedAsync(string filePath)
    {
        var uri = PathToUri(filePath);
        _documentVersionsByUri.Remove(uri);

        return SendNotificationAsync("textDocument/didClose", new JObject
        {
            ["textDocument"] = new JObject { ["uri"] = uri },
        });
    }

    public async Task<IReadOnlyList<LspLocation>> GetDefinitionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("textDocument/definition", TextDocumentPositionParams(filePath, position), cancellationToken);
        return ParseLocations(result);
    }

    public async Task<IReadOnlyList<LspLocation>> GetReferencesAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default)
    {
        var parameters = TextDocumentPositionParams(filePath, position);
        parameters["context"] = new JObject { ["includeDeclaration"] = true };

        var result = await SendRequestAsync("textDocument/references", parameters, cancellationToken);
        return ParseLocations(result);
    }

    public async Task<IReadOnlyList<LspCompletionItem>> GetCompletionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("textDocument/completion", TextDocumentPositionParams(filePath, position), cancellationToken);
        return ParseCompletionItems(result);
    }

    private static JObject TextDocumentPositionParams(string filePath, LspPosition position) => new()
    {
        ["textDocument"] = new JObject { ["uri"] = PathToUri(filePath) },
        ["position"] = new JObject { ["line"] = position.Line, ["character"] = position.Character },
    };

    // Result is either a single Location, a Location[], or null (nothing found) - LSP's
    // textDocument/definition is explicitly allowed to return any of these three shapes.
    private static IReadOnlyList<LspLocation> ParseLocations(JToken? result)
    {
        IEnumerable<JToken> tokens = result switch
        {
            null => [],
            JArray array => array,
            JObject single => [single],
            _ => [],
        };

        var locations = new List<LspLocation>();

        foreach (var token in tokens)
        {
            if (token is not JObject location || location["uri"]?.ToString() is not { } uri || location["range"]?["start"] is not JObject start)
            {
                continue;
            }

            locations.Add(new LspLocation(UriToPath(uri), new LspPosition(start["line"]?.ToObject<int>() ?? 0, start["character"]?.ToObject<int>() ?? 0)));
        }

        return locations;
    }

    // Result is either a bare CompletionItem[] or a CompletionList { isIncomplete, items: [...] } -
    // the LSP spec allows a server to return either (R2.2). isIncomplete isn't surfaced to callers:
    // v1 requests completions exactly once per trigger (R1.4) and never re-queries to page in more
    // results.
    private static IReadOnlyList<LspCompletionItem> ParseCompletionItems(JToken? result)
    {
        var itemsArray = result switch
        {
            JArray array => array,
            JObject list => list["items"] as JArray,
            _ => null,
        };

        if (itemsArray is null)
        {
            return [];
        }

        var items = new List<LspCompletionItem>();

        foreach (var token in itemsArray)
        {
            if (token is not JObject item || item["label"]?.ToString() is not { } label)
            {
                continue;
            }

            items.Add(new LspCompletionItem(label, item["detail"]?.ToString(), item["insertText"]?.ToString(), item["kind"]?.ToObject<int?>()));
        }

        return items;
    }

    private async Task<JToken?> SendRequestAsync(string method, JObject? parameters, CancellationToken cancellationToken)
    {
        var process = _process ?? throw new InvalidOperationException("The language server is not running.");
        var id = Interlocked.Increment(ref _nextRequestId);

        var completionSource = new TaskCompletionSource<JToken?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingRequests)
        {
            _pendingRequests[id] = completionSource;
        }

        var message = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters,
        };

        await WriteMessageAsync(process, message);

        using var registration = cancellationToken.Register(() =>
        {
            lock (_pendingRequests)
            {
                _pendingRequests.Remove(id);
            }

            completionSource.TrySetCanceled();
        });

        return await completionSource.Task;
    }

    private async Task SendNotificationAsync(string method, JObject? parameters)
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        await WriteMessageAsync(process, new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters,
        });
    }

    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    private static async Task WriteMessageAsync(Process process, JObject message)
    {
        var body = Encoding.UTF8.GetBytes(message.ToString(Newtonsoft.Json.Formatting.None));
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

        await WriteLock.WaitAsync();
        try
        {
            var stream = process.StandardInput.BaseStream;
            await stream.WriteAsync(header);
            await stream.WriteAsync(body);
            await stream.FlushAsync();
        }
        finally
        {
            WriteLock.Release();
        }
    }

    private async Task ReadLoopAsync(Stream stdout, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var contentLength = await ReadContentLengthAsync(stdout, cancellationToken);
                if (contentLength is null)
                {
                    break;
                }

                var body = new byte[contentLength.Value];
                await ReadExactAsync(stdout, body, cancellationToken);

                JObject message;
                try
                {
                    message = JObject.Parse(Encoding.UTF8.GetString(body));
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    continue;
                }

                DispatchMessage(message);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task DrainStandardErrorAsync(Stream stderr, CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new byte[4096];
            while (await stderr.ReadAsync(buffer, cancellationToken) > 0)
            {
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    // Reads header lines up to the blank line that ends the frame, per the LSP base protocol - only
    // Content-Length is required/used; any other header (e.g. Content-Type) is read and ignored.
    private static async Task<int?> ReadContentLengthAsync(Stream stream, CancellationToken cancellationToken)
    {
        int? contentLength = null;

        while (true)
        {
            var line = await ReadHeaderLineAsync(stream, cancellationToken);
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                return contentLength;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex > 0 && line[..separatorIndex].Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                contentLength = int.Parse(line[(separatorIndex + 1)..].Trim());
            }
        }
    }

    private static async Task<string?> ReadHeaderLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            }

            if (buffer[0] == '\n')
            {
                // Trim a preceding \r (the header terminator is \r\n, but tolerate a bare \n too).
                if (bytes.Count > 0 && bytes[^1] == '\r')
                {
                    bytes.RemoveAt(bytes.Count - 1);
                }

                return Encoding.ASCII.GetString(bytes.ToArray());
            }

            bytes.Add(buffer[0]);
        }
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                throw new IOException("The language server closed its output stream unexpectedly.");
            }

            offset += read;
        }
    }

    private void DispatchMessage(JObject message)
    {
        if (message["id"]?.ToObject<int?>() is { } id)
        {
            TaskCompletionSource<JToken?>? completionSource;
            lock (_pendingRequests)
            {
                if (!_pendingRequests.Remove(id, out completionSource))
                {
                    return;
                }
            }

            completionSource.TrySetResult(message["result"]);
            return;
        }

        if (message["method"]?.ToString() == "textDocument/publishDiagnostics" && message["params"] is JObject diagnosticsParams)
        {
            DispatchDiagnostics(diagnosticsParams);
        }
    }

    private void DispatchDiagnostics(JObject parameters)
    {
        if (parameters["uri"]?.ToString() is not { } uri || parameters["diagnostics"] is not JArray diagnosticsArray)
        {
            return;
        }

        var diagnostics = new List<LspDiagnostic>();

        foreach (var token in diagnosticsArray)
        {
            if (token is not JObject diagnostic || diagnostic["range"] is not JObject range || range["start"] is not JObject start || range["end"] is not JObject end)
            {
                continue;
            }

            var startPosition = new LspPosition(start["line"]?.ToObject<int>() ?? 0, start["character"]?.ToObject<int>() ?? 0);
            var endPosition = new LspPosition(end["line"]?.ToObject<int>() ?? 0, end["character"]?.ToObject<int>() ?? 0);
            var severity = (diagnostic["severity"]?.ToObject<int?>()) switch
            {
                1 => LspDiagnosticSeverity.Error,
                2 => LspDiagnosticSeverity.Warning,
                3 => LspDiagnosticSeverity.Information,
                _ => LspDiagnosticSeverity.Hint,
            };

            diagnostics.Add(new LspDiagnostic(startPosition, endPosition, diagnostic["message"]?.ToString() ?? string.Empty, severity));
        }

        DiagnosticsPublished?.Invoke(UriToPath(uri), diagnostics);
    }

    private void FailAllPendingRequests()
    {
        List<TaskCompletionSource<JToken?>> pending;
        lock (_pendingRequests)
        {
            pending = _pendingRequests.Values.ToList();
            _pendingRequests.Clear();
        }

        foreach (var completionSource in pending)
        {
            completionSource.TrySetCanceled();
        }
    }

    private static string PathToUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private static string UriToPath(string uri) => Uri.UnescapeDataString(new Uri(uri).LocalPath);

    public async ValueTask DisposeAsync() => await StopAsync();
}
