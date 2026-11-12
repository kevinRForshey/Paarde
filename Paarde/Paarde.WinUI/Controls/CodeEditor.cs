using System.Text.Json;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;

using Paarde.Core.Models;
using Paarde.Presentation.Helpers;

namespace Paarde.WinUI.Controls;

// Raised when the gutter's Accept/Reject glyph for a hunk is clicked.
public sealed class DiffHunkAcceptRejectEventArgs(DiffHunk hunk, bool accept) : EventArgs
{
    public DiffHunk Hunk { get; } = hunk;

    public bool Accept { get; } = accept;
}

// F12 / Shift+F12 - the host decides what to ask the language server.
public sealed class CodeNavigationRequestedEventArgs(LspPosition position) : EventArgs
{
    public LspPosition Position { get; } = position;
}

// The WinUI code editor - Monaco (MIT) in WebView2, chosen by the F31.6
// spike (design.md "Code editor"). Unlike Paarde.Avalonia's CodeEditor (one AvaloniaEdit instance
// per open tab, everything bound), one of these serves every tab of an Explorer tab group: each
// open file is its own Monaco model, and the host calls ShowDocument as the group's tab changes.
// A WebView2 costs ~90 MB, so one per tab wasn't an option. The API is therefore imperative
// (ShowDocument/UpdateText/SetDiagnostics/SetDiff) and keyed by file path, and the request/response
// features (completion, hover, ghost text) are async provider delegates rather than Avalonia's
// "raise an event, wait for a bound property to change" pair, since Monaco awaits each answer.
public sealed partial class CodeEditor : UserControl
{
    public static readonly DependencyProperty CaretPositionProperty =
        DependencyProperty.Register(nameof(CaretPosition), typeof(LspPosition), typeof(CodeEditor), new PropertyMetadata(null));

    // Shared by every editor so they run in one browser process group. Set by App before any
    // editor is created; null falls back to WebView2's default (next to the executable).
    public static string? UserDataFolder { get; set; }

    private static Task<CoreWebView2Environment>? s_environment;

    private readonly WebView2 _webView = new();

    // Script calls made before Monaco has loaded run, in order, once it reports ready.
    private readonly List<string> _pendingScripts = [];
    private readonly Dictionary<string, IReadOnlyList<DiffHunk>> _hunksByPath = new(StringComparer.Ordinal);
    private bool _isReady;
    private bool _isInitializing;
    private bool _isClosed;

    public LspPosition? CaretPosition
    {
        get => (LspPosition?)GetValue(CaretPositionProperty);
        private set => SetValue(CaretPositionProperty, value);
    }

    // The file whose model is currently shown, or null.
    public string? DocumentPath { get; private set; }

    public bool HasEditorFocus { get; private set; }

    // XAML's view of focus, which stays on the WebView2 while the window is inactive.
    public bool HasXamlFocus => XamlRoot is { } root && ReferenceEquals(FocusManager.GetFocusedElement(root), _webView);

    public Func<LspPosition, Task<IReadOnlyList<LspCompletionItem>?>>? CompletionProvider { get; set; }

    public Func<LspPosition, Task<LspHover?>>? HoverProvider { get; set; }

    // (text before the caret, text after it) -> the suggestion to show, or null.
    public Func<string, string, Task<string?>>? GhostTextProvider { get; set; }

    // Raised for edits typed in the editor, never for text the host pushed in.
    public event Action<string, string>? TextEdited;

    public event EventHandler? EditorGotFocus;

    public event EventHandler? EditorLostFocus;

    public event EventHandler<DiffHunkAcceptRejectEventArgs>? DiffHunkActionRequested;

    public event EventHandler<CodeNavigationRequestedEventArgs>? GoToDefinitionRequested;

    public event EventHandler<CodeNavigationRequestedEventArgs>? FindReferencesRequested;

    // Keys the app owns but WebView2 swallows: "Ctrl+P", "Ctrl+Shift+P", "Ctrl+Shift+O",
    // "Ctrl+Tab", "CtrlReleased", "Ctrl+K Z".
    public event Action<string>? ShortcutForwarded;

    public CodeEditor()
    {
        Content = _webView;
        Loaded += OnLoaded;
        ActualThemeChanged += (_, _) => Call("setTheme", ActualTheme != ElementTheme.Light);
    }

    public void ShowDocument(string filePath, string text)
    {
        DocumentPath = filePath;
        Call("showDocument", filePath, text);
    }

    public void ClearDocument()
    {
        DocumentPath = null;
        CaretPosition = null;
        Call("clearDocument");
    }

    // Text that changed outside the editor (Revert, an edit made from Review). A no-op if Monaco
    // already has exactly this text.
    public void UpdateText(string filePath, string text) => Call("updateText", filePath, text);

    public void CloseDocument(string filePath)
    {
        _hunksByPath.Remove(filePath);
        if (filePath == DocumentPath)
        {
            DocumentPath = null;
        }

        Call("closeDocument", filePath);
    }

    public void SetDiagnostics(string filePath, IReadOnlyList<LspDiagnostic>? diagnostics) =>
        Call("setDiagnostics", filePath, (diagnostics ?? []).Select(d => new
        {
            severity = (int)d.Severity,
            message = d.Message,
            startLine = d.Start.Line,
            startCharacter = d.Start.Character,
            endLine = d.End.Line,
            endCharacter = d.End.Character,
        }));

    // DiffLines -> added bands and deletion markers (DiffBandBuilder, shared with Avalonia's
    // DiffBackgroundRenderer); DiffHunks -> one Accept/Reject glyph pair at each hunk's
    // ProposedStartLine (clamped to at least 1, DiffHunkGutterMargin's convention).
    public void SetDiff(string filePath, IReadOnlyList<DiffLine>? diffLines, IReadOnlyList<DiffHunk>? hunks)
    {
        _hunksByPath[filePath] = hunks ?? [];
        var bands = diffLines is null ? [] : DiffBandBuilder.Build(diffLines);
        Call(
            "setDiff",
            filePath,
            bands.Select(b => new { startLine = b.StartLine, lineCount = b.LineCount, isAddition = b.IsAddition }),
            (hunks ?? []).Select(h => Math.Max(h.ProposedStartLine, 1)));
    }

    public void RevealPosition(LspPosition position, bool focus) => Call("reveal", position.Line, position.Character, focus);

    public void FocusEditor()
    {
        _webView.Focus(FocusState.Programmatic);
        Call("focus");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _isReady || _isClosed)
        {
            return;
        }

        _isInitializing = true;
        s_environment ??= CreateEnvironmentAsync();
        await _webView.EnsureCoreWebView2Async(await s_environment);

        var core = _webView.CoreWebView2;

        // No browser shortcuts (Ctrl+P print, Ctrl+F find-in-page, F5 reload...); Monaco has its own
        // find, and the app's own shortcuts are forwarded by the page.
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        // Served from the output folder through a virtual host: no network, no file:// URLs.
        core.SetVirtualHostNameToFolderMapping("paarde.editor", Path.Combine(AppContext.BaseDirectory, "Editor"), CoreWebView2HostResourceAccessKind.Deny);
        core.WebMessageReceived += OnWebMessageReceived;
        core.Navigate("https://paarde.editor/index.html");

        // WebView2 keeps running until it's closed, and the hosting page is never reused.
        Unloaded += (_, _) =>
        {
            _isClosed = true;
            _webView.Close();
        };
    }

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync() =>
        await CoreWebView2Environment.CreateWithOptionsAsync(string.Empty, UserDataFolder ?? string.Empty, new CoreWebView2EnvironmentOptions());

    private async void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        using var document = JsonDocument.Parse(args.WebMessageAsJson);
        var message = document.RootElement;

        switch (message.GetProperty("type").GetString())
        {
            case "ready":
                // Drained one at a time: calls made while a queued one is running join the end of
                // the queue rather than overtaking it.
                Call("setTheme", ActualTheme != ElementTheme.Light);
                while (_pendingScripts.Count > 0)
                {
                    var script = _pendingScripts[0];
                    _pendingScripts.RemoveAt(0);
                    await ExecuteScriptAsync(script);
                }

                _isReady = true;
                break;

            case "textChanged":
                if (message.GetProperty("path").GetString() is { } path)
                {
                    TextEdited?.Invoke(path, message.GetProperty("text").GetString() ?? string.Empty);
                }

                break;

            case "caret":
                CaretPosition = DocumentPath is null ? null : ReadPosition(message);
                break;

            case "focus":
                HasEditorFocus = true;
                EditorGotFocus?.Invoke(this, EventArgs.Empty);
                break;

            case "blur":
                HasEditorFocus = false;
                EditorLostFocus?.Invoke(this, EventArgs.Empty);
                break;

            case "hunkAction":
                var index = message.GetProperty("index").GetInt32();
                if (DocumentPath is not null && _hunksByPath.TryGetValue(DocumentPath, out var hunks) && index < hunks.Count)
                {
                    DiffHunkActionRequested?.Invoke(this, new DiffHunkAcceptRejectEventArgs(hunks[index], message.GetProperty("accept").GetBoolean()));
                }

                break;

            case "definition":
                GoToDefinitionRequested?.Invoke(this, new CodeNavigationRequestedEventArgs(ReadPosition(message)));
                break;

            case "references":
                FindReferencesRequested?.Invoke(this, new CodeNavigationRequestedEventArgs(ReadPosition(message)));
                break;

            case "shortcut":
                ShortcutForwarded?.Invoke(message.GetProperty("gesture").GetString() ?? string.Empty);
                break;

            case "request":
                await AnswerRequestAsync(message);
                break;

            case "error":
                // Script errors in the editor page itself; surfaced in the Debug output only.
                System.Diagnostics.Debug.WriteLine($"CodeEditor script error: {message.GetProperty("message").GetString()}");
                break;
        }
    }

    // Every request gets an answer (null when there's no provider or nothing to show), so Monaco
    // never waits on a promise that won't settle.
    private async Task AnswerRequestAsync(JsonElement message)
    {
        var id = message.GetProperty("id").GetInt32();
        object? answer = null;

        switch (message.GetProperty("kind").GetString())
        {
            case "completion" when CompletionProvider is { } completionProvider:
                answer = (await completionProvider(ReadPosition(message)))?.Select(item => new
                {
                    label = item.Label,
                    detail = item.Detail,
                    insertText = item.InsertText,
                    kind = item.Kind,
                });
                break;

            case "hover" when HoverProvider is { } hoverProvider:
                answer = (await hoverProvider(ReadPosition(message)))?.Contents;
                break;

            case "ghost" when GhostTextProvider is { } ghostTextProvider:
                answer = await ghostTextProvider(message.GetProperty("before").GetString() ?? string.Empty, message.GetProperty("after").GetString() ?? string.Empty);
                break;
        }

        Call("resolve", id, answer);
    }

    private static LspPosition ReadPosition(JsonElement message) =>
        new(message.GetProperty("line").GetInt32(), message.GetProperty("character").GetInt32());

    private async void Call(string function, params object?[] arguments)
    {
        var script = $"paarde.{function}({string.Join(",", arguments.Select(a => JsonSerializer.Serialize(a)))})";

        if (_isClosed)
        {
            return;
        }

        if (!_isReady)
        {
            _pendingScripts.Add(script);
            return;
        }

        await ExecuteScriptAsync(script);
    }

    // Calls are fire-and-forget (async void), so a failure here would take the whole app down.
    // The usual one is ERROR_INVALID_STATE (0x8007139F): the WebView2 was closed (tab/page
    // unloaded, app shutting down) or its browser process died while a call - often a slow
    // completion/hover/ghost-text answer - was still in flight. The editor is gone at that point,
    // so there's nothing to deliver the script to; drop it.
    private async Task ExecuteScriptAsync(string script)
    {
        if (_isClosed || _webView.CoreWebView2 is not { } core)
        {
            return;
        }

        try
        {
            await core.ExecuteScriptAsync(script);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ObjectDisposedException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"CodeEditor: dropped script, WebView2 not available ({ex.Message.Trim()}).");
        }
    }
}
