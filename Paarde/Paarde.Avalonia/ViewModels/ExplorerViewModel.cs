using Avalonia.Threading;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Contracts.ViewModels;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.Strings;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Paarde.Avalonia.ViewModels;

// T8.1.1: project explorer - a gitignore-aware file-tree view of the working directory last
// picked on the Prompt page (same persisted setting, via WorkingDirectorySettingsKeys, rather
// than IPromptResultStore.WorkingDirectoryPath - the tree should be browsable as soon as a folder
// is picked, not only after a prompt has actually been run).
public partial class ExplorerViewModel : ObservableObject, INavigationAware, IConfirmNavigationAway
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IPromptResultStore _promptResultStore;
    private readonly ILlmRunner _llmRunner;
    private readonly IFileChangeApplyService _fileChangeApplyService;
    private readonly IDialogService _dialogService;
    private readonly ILanguageServerSessionManager _languageServerSessionManager;
    private readonly ILogger<ExplorerViewModel> _logger;

    // Epic 17, T17.1.3: set by NavigateToLocation, consumed once by ExplorerView.axaml.cs's
    // Editor.TextChanged handler - same "take once, after the target content has already loaded"
    // pull pattern TutorialViewModel.TakePendingStepTarget() already established, needed because a
    // cross-file jump only reflects into Editor.Text after SelectedFileContent's binding runs.
    private LspPosition? _pendingCaretTarget;

    // T10.2.1: what's actually on disk as of the last successful load/save, compared against
    // SelectedFileContent to derive IsFileDirty - same IsDirty-via-divergence shape
    // ProposedFileChangeViewModel already uses, just without a third (proposed) baseline since
    // there's no LLM proposal in this flow.
    private string _originalFileContentOnDisk = string.Empty;

    // T10.2.5: captured by OnSelectedNodeChanging (fires before SelectedNode's backing field is
    // updated) so a declined "discard unsaved edits?" prompt can put the tree's selection back
    // where it was.
    private FileTreeNodeViewModel? _nodeBeforeSelectionChange;

    [ObservableProperty]
    private string workingDirectoryPath = string.Empty;

    [ObservableProperty]
    private FileTreeNodeViewModel? root;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private FileTreeNodeViewModel? selectedNode;

    [ObservableProperty]
    private string selectedFilePath = string.Empty;

    [ObservableProperty]
    private string selectedFileContent = string.Empty;

    // T10.1.1: the AI-generated tutorial-style explanation of SelectedFilePath, requested on demand
    // via ExplainSelectedFileCommand rather than automatically on every selection - running a prompt
    // on every tree click would be surprising cost/latency, same reasoning
    // TutorialViewModel.RefineSelectedFileCommand/TutorialStepViewModel.ExplainFurtherCommand are
    // both explicit actions rather than eager.
    [ObservableProperty]
    private string fileOverview = string.Empty;

    [ObservableProperty]
    private bool isLoadingOverview;

    // Epic 15, T15.2: the most recent ghost-text completion CodeEditor asked for - set to null then
    // immediately to the new value (rather than straight to the new value) so a repeat suggestion
    // (same text twice in a row) still raises PropertyChanged; Avalonia's property system otherwise
    // short-circuits a set that equals the current value, and CodeEditor would never redisplay it.
    [ObservableProperty]
    private string? ghostTextSuggestion;

    private CancellationTokenSource? _ghostTextCancellationTokenSource;

    // T10.2.1: whether SelectedFileContent has diverged from _originalFileContentOnDisk.
    [ObservableProperty]
    private bool isFileDirty;

    // Epic 17, T17.1.2: the active file's diagnostics, pushed by the language server and filtered
    // in OnDiagnosticsPublished to whichever file is actually open right now - null (not empty)
    // distinguishes "no diagnostics received yet for this file" from "server reported zero".
    [ObservableProperty]
    private IReadOnlyList<LspDiagnostic>? diagnostics;

    // specs/editor-autocomplete T4: the most recent completion list CodeEditor asked for - same
    // null-then-value double assignment GhostTextSuggestion above uses, for the same reason (a
    // repeat trigger at the same offset would otherwise short-circuit and never redisplay).
    [ObservableProperty]
    private IReadOnlyList<LspCompletionItem>? completionItems;

    public ExplorerViewModel(
        ILocalSettingsService localSettingsService,
        IPromptResultStore promptResultStore,
        ILlmRunner llmRunner,
        IFileChangeApplyService fileChangeApplyService,
        IDialogService dialogService,
        ILanguageServerSessionManager languageServerSessionManager,
        ILogger<ExplorerViewModel>? logger = null)
    {
        _promptResultStore = promptResultStore;
        _localSettingsService = localSettingsService;
        _llmRunner = llmRunner;
        _fileChangeApplyService = fileChangeApplyService;
        _dialogService = dialogService;
        _languageServerSessionManager = languageServerSessionManager;
        _logger = logger ?? NullLogger<ExplorerViewModel>.Instance;

        _languageServerSessionManager.DiagnosticsPublished += OnDiagnosticsPublished;
    }

    // T10.2.5: IConfirmNavigationAway - checked by NavigationService.OnNavigating before leaving
    // this page, same mechanism TutorialViewModel already uses for its own unsaved edits.
    public bool HasUnsavedChanges => IsFileDirty;

    partial void OnSelectedNodeChanging(FileTreeNodeViewModel? oldValue, FileTreeNodeViewModel? newValue) =>
        _nodeBeforeSelectionChange = oldValue;

    // T8.1.3: open any file in the editor, proposed or not - a read-only preview, deliberately not
    // wired into ProposedFileChangeViewModel.EditedContent for touched files, so Explorer stays a
    // browse/view surface and editing a proposal stays the Tutorial page's job.
    //
    // T10.2.5: switching the tree selection is not a page navigation, so IConfirmNavigationAway's
    // guard above never sees it - this needs its own unsaved-edits check. Re-entrancy guard first:
    // reselecting the node that's already loaded (including the revert this method itself performs
    // below when the user declines to discard) must not re-read from disk and wipe out the very
    // edits being protected.
    partial void OnSelectedNodeChanged(FileTreeNodeViewModel? value)
    {
        var newPath = value is { IsDirectory: false } ? value.FullPath : string.Empty;

        if (newPath == SelectedFilePath)
        {
            return;
        }

        if (IsFileDirty)
        {
            ConfirmDiscardDirtyFileThenLoad(value);
            return;
        }

        LoadNode(value);
    }

    private async void ConfirmDiscardDirtyFileThenLoad(FileTreeNodeViewModel? value)
    {
        try
        {
            var discard = await _dialogService.ConfirmAsync(
                "Explorer_DiscardEditsConfirmTitle".Resolve(),
                "Explorer_DiscardEditsConfirmMessage".Resolve(),
                "Explorer_DiscardEditsConfirmButton".Resolve(),
                "Explorer_DiscardEditsCancelButton".Resolve());

            if (discard)
            {
                LoadNode(value);
            }
            else
            {
                SelectedNode = _nodeBeforeSelectionChange;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm discarding unsaved Explorer edits.");
        }
    }

    private void LoadNode(FileTreeNodeViewModel? value)
    {
        // T10.1.3: a previous selection's explanation would otherwise linger and read as if it
        // described whatever's selected now.
        FileOverview = string.Empty;

        // Epic 17, T17.2.1: tells the server the previously open file is gone before (potentially)
        // opening a new one - a no-op if nothing was open or the server isn't running.
        if (!string.IsNullOrEmpty(SelectedFilePath))
        {
            _ = _languageServerSessionManager.NotifyDocumentClosedAsync(SelectedFilePath);
        }

        Diagnostics = null;

        if (value is null || value.IsDirectory)
        {
            SelectedFilePath = string.Empty;
            _originalFileContentOnDisk = string.Empty;
            SelectedFileContent = string.Empty;
            return;
        }

        SelectedFilePath = value.FullPath;
        _originalFileContentOnDisk = FileContentReader.ReadOrEmpty(value.FullPath);
        SelectedFileContent = _originalFileContentOnDisk;

        _ = _languageServerSessionManager.NotifyDocumentOpenedAsync(SelectedFilePath, SelectedFileContent);
    }

    partial void OnSelectedFilePathChanged(string value) => ExplainSelectedFileCommand.NotifyCanExecuteChanged();

    partial void OnSelectedFileContentChanged(string value)
    {
        IsFileDirty = value != _originalFileContentOnDisk;

        if (!string.IsNullOrEmpty(SelectedFilePath))
        {
            _ = _languageServerSessionManager.NotifyDocumentChangedAsync(SelectedFilePath, value);
        }
    }

    partial void OnIsLoadingOverviewChanged(bool value) => ExplainSelectedFileCommand.NotifyCanExecuteChanged();

    partial void OnIsFileDirtyChanged(bool value)
    {
        SaveSelectedFileCommand.NotifyCanExecuteChanged();
        RevertSelectedFileToDiskCommand.NotifyCanExecuteChanged();
    }

    // T10.2.2: writes through IFileChangeApplyService.WriteRaw rather than a raw File.WriteAllText,
    // so a manual save gets the same path-containment check (FilePathContainment.Resolve, T4.1.1)
    // and encoding/line-ending preservation (FileEncodingDetector, T4.1.3) every other write in this
    // app already gets.
    [RelayCommand(CanExecute = nameof(CanSaveSelectedFile))]
    private void SaveSelectedFile()
    {
        var relativePath = Path.GetRelativePath(WorkingDirectoryPath, SelectedFilePath).Replace(Path.DirectorySeparatorChar, '/');

        // T10.2.3: refuses outright rather than offering an "apply anyway" override - unlike the
        // batch apply flow (which already has a confirm-dialog service wired for exactly that),
        // this is a single ad-hoc edit and a narrower write path that doesn't need to reuse
        // allowProtectedPaths' override semantics on day one.
        if (ProtectedPathChecker.IsProtected(WorkingDirectoryPath, relativePath))
        {
            StatusMessage = string.Format("Explorer_SaveBlockedProtectedPath".Resolve(), relativePath);
            return;
        }

        // T10.2.4: the same check ApplyOrchestrationService.ApplyOne does before an LLM apply
        // (currentDiskContent != file.OriginalContent, T3.3.3) - if the file changed on disk since
        // it was opened here, refuse rather than silently clobbering whatever wrote it.
        var currentDiskContent = FileContentReader.ReadOrEmpty(SelectedFilePath);

        if (currentDiskContent != _originalFileContentOnDisk)
        {
            StatusMessage = string.Format("Explorer_SaveConflict".Resolve(), relativePath);
            return;
        }

        try
        {
            _fileChangeApplyService.WriteRaw(relativePath, SelectedFileContent, WorkingDirectoryPath);
            _originalFileContentOnDisk = SelectedFileContent;
            IsFileDirty = false;
            StatusMessage = string.Format("Explorer_SaveSucceeded".Resolve(), relativePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to save {FilePath} in {WorkingDirectory}.", relativePath, WorkingDirectoryPath);
            StatusMessage = string.Format("Explorer_SaveFailed".Resolve(), relativePath, ex.Message);
        }
    }

    private bool CanSaveSelectedFile() => IsFileDirty;

    // T10.2.6: Undo/Redo themselves are free once CodeEditor stops being read-only (AvaloniaEdit's
    // TextAreaDefaultInputHandler already wires Ctrl+Z/Ctrl+Y) - this covers the case undo history
    // doesn't: discarding every edit at once and going back to what's on disk right now, which may
    // have changed since this file was opened.
    [RelayCommand(CanExecute = nameof(CanRevertSelectedFileToDisk))]
    private void RevertSelectedFileToDisk()
    {
        _originalFileContentOnDisk = FileContentReader.ReadOrEmpty(SelectedFilePath);
        SelectedFileContent = _originalFileContentOnDisk;
    }

    private bool CanRevertSelectedFileToDisk() => IsFileDirty;

    // T10.1.1/T10.1.2: reuses the plain-prose prompt pattern TutorialStepViewModel.ExplainFurtherAsync
    // already established (no JSON, no markdown fences - this is prose for a human, not something
    // ClaudeJsonResponseParser needs to parse). No resumeSessionId - this is a one-off lookup against
    // whatever file is open in Explorer, not a continuation of the active tutorial's conversation.
    [RelayCommand(CanExecute = nameof(CanExplainSelectedFile))]
    private async Task ExplainSelectedFileAsync()
    {
        IsLoadingOverview = true;
        FileOverview = string.Empty;

        try
        {
            var prompt =
                "Explain this file in a tutorial style - what it does, how it's structured, and " +
                "anything a newcomer to this codebase should notice. Respond in plain prose only - " +
                "no JSON, no markdown fences.\n\n" +
                $"File: {SelectedFilePath}\n\n{SelectedFileContent}";

            var result = await _llmRunner.RunPromptAsync(prompt, WorkingDirectoryPath);

            FileOverview = result.Success
                ? result.Output.Trim()
                : string.Format("Explorer_ExplainFailed".Resolve(), result.ErrorMessage);
        }
        finally
        {
            IsLoadingOverview = false;
        }
    }

    private bool CanExplainSelectedFile() => !IsLoadingOverview && !string.IsNullOrEmpty(SelectedFilePath);

    // Epic 15, T15.2.1/T15.2.2: handles CodeEditor.GhostTextRequested. Deliberately reuses whichever
    // ILlmRunner is already configured (same as ExplainSelectedFileAsync above) rather than adding a
    // dedicated fast-model setting for completions - latency will vary with the active runner (a
    // ClaudeCli process spawn is a lot slower than a direct API call), which is a real, currently
    // unverified-in-this-sandbox UX risk the roadmap flagged as an open question (F15.2.2). The
    // short per-request timeout plus CodeEditor's own staleness check (dropping a suggestion whose
    // caret offset no longer matches) are the mitigation for now; a dedicated faster model is the
    // follow-up if this proves too slow in practice, not something to build ahead of that evidence.
    //
    // No resumeSessionId - a one-off lookup against whatever's open in Explorer, not a continuation
    // of any other conversation, same reasoning ExplainSelectedFileAsync's comment gives.
    public async Task RequestGhostTextCompletionAsync(string textBeforeCaret, string textAfterCaret)
    {
        _ghostTextCancellationTokenSource?.Cancel();
        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        _ghostTextCancellationTokenSource = cancellationTokenSource;

        try
        {
            var prompt =
                "Complete the code at the <cursor/> marker below. Respond with ONLY the text that " +
                "should be inserted at the cursor - no explanation, no markdown fences, nothing before " +
                "or after it. If nothing reasonable completes the code there, respond with nothing.\n\n" +
                $"File: {SelectedFilePath}\n\n{textBeforeCaret}<cursor/>{textAfterCaret}";

            var result = await _llmRunner.RunPromptAsync(prompt, WorkingDirectoryPath, cancellationToken: cancellationTokenSource.Token);
            if (cancellationTokenSource.IsCancellationRequested || !result.Success || string.IsNullOrWhiteSpace(result.Output))
            {
                return;
            }

            GhostTextSuggestion = null;
            GhostTextSuggestion = result.Output.Trim();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request, or timed out - either way, there's nothing to show.
        }
    }

    // T9.2.3: async void because INavigationAware.OnNavigatedTo returns void - wrapped so a
    // corrupt settings file (the one thing ReadSettingAsync can actually throw on) is logged
    // rather than unwound unobserved out of NavigationService.OnNavigated; RefreshTree's own
    // try/catch below already covers everything that can go wrong once WorkingDirectoryPath is in
    // hand.
    public async void OnNavigatedTo(object parameter)
    {
        try
        {
            WorkingDirectoryPath = await _localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory) ?? string.Empty;
            RefreshTree();

            // Epic 17, T17.2.1/Epic 18: lifecycle tied to Explorer's working directory -
            // StartAllAsync itself detects which configured languages are relevant here (project
            // markers) and skips any whose executable isn't installed, per-language, rather than
            // this call site gating on a single IsAvailableAsync check.
            if (!string.IsNullOrWhiteSpace(WorkingDirectoryPath) && Directory.Exists(WorkingDirectoryPath))
            {
                await _languageServerSessionManager.StartAllAsync(WorkingDirectoryPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the saved working directory while navigating to the Explorer page.");
        }
    }

    // Epic 17, T17.2.1: ExplorerViewModel is AddTransient (App.axaml.cs) - a fresh instance is
    // constructed on every navigation to this page, but DiagnosticsPublished is raised by the
    // singleton ILanguageServerClient, so the subscription taken in the constructor must be
    // unhooked here or it accumulates one stale handler per visit to this page. The server process
    // itself keeps running (teardown is app-exit only, per the approved scope) - only the event
    // subscription is page-navigation-scoped.
    public void OnNavigatedFrom() => _languageServerSessionManager.DiagnosticsPublished -= OnDiagnosticsPublished;

    // Epic 17, T17.1.2: fires on the server's notification-read background thread - marshaled to
    // the UI thread before touching Diagnostics, same caveat ILlmRunner's onTextDelta callbacks
    // already carry. Filtered to whichever file is open right now; a push for any other file is
    // silently dropped since nothing in this VM surfaces anything but the active file's diagnostics.
    private void OnDiagnosticsPublished(string filePath, IReadOnlyList<LspDiagnostic> publishedDiagnostics) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (string.Equals(filePath, SelectedFilePath, StringComparison.Ordinal))
            {
                Diagnostics = publishedDiagnostics;
            }
        });

    // Epic 17, T17.1.3: go-to-definition - navigates straight to the first result, same "don't
    // build a disambiguation UI for the common single-result case" scope decision FindReferencesAsync
    // below explains for its own (genuinely multi-result) case.
    public async Task GoToDefinitionAsync(LspPosition position)
    {
        if (string.IsNullOrEmpty(SelectedFilePath))
        {
            return;
        }

        try
        {
            var locations = await _languageServerSessionManager.GetDefinitionAsync(SelectedFilePath, position);
            if (locations.Count > 0)
            {
                NavigateToLocation(locations[0]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Go-to-definition failed for {FilePath}.", SelectedFilePath);
        }
    }

    // Epic 17, T17.1.3: find-references - returns every result for ExplorerView's minimal flyout to
    // list, rather than jumping straight to one, since references (unlike a definition) are
    // routinely more than one and there's no single "right" result to prefer.
    public async Task<IReadOnlyList<LspLocation>> FindReferencesAsync(LspPosition position)
    {
        if (string.IsNullOrEmpty(SelectedFilePath))
        {
            return Array.Empty<LspLocation>();
        }

        try
        {
            return await _languageServerSessionManager.GetReferencesAsync(SelectedFilePath, position);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Find-references failed for {FilePath}.", SelectedFilePath);
            return Array.Empty<LspLocation>();
        }
    }

    // specs/editor-autocomplete T4: handles CodeEditor.CompletionRequested. Unlike
    // RequestGhostTextCompletionAsync above, this goes straight to the language server (not the
    // LLM runner) - R2's whole point is a server-verified completion list, not a guess.
    public async Task RequestCompletionAsync(LspPosition position)
    {
        if (string.IsNullOrEmpty(SelectedFilePath))
        {
            return;
        }

        try
        {
            var items = await _languageServerSessionManager.GetCompletionAsync(SelectedFilePath, position);
            CompletionItems = null;
            CompletionItems = items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Completion request failed for {FilePath}.", SelectedFilePath);
        }
    }

    // Epic 17, T17.1.3: jumps to location, switching the tree selection first if it's in a
    // different file (reusing the existing SelectedNode -> LoadNode flow, including its dirty-file
    // guard) before leaving the caret position for ExplorerView to apply via TakePendingCaretTarget.
    public void NavigateToLocation(LspLocation location)
    {
        _pendingCaretTarget = location.Position;

        if (string.Equals(location.FilePath, SelectedFilePath, StringComparison.Ordinal))
        {
            return;
        }

        if (Root is not null && FileTreeNodeViewModel.FindByPath(Root, location.FilePath) is { } node)
        {
            SelectedNode = node;
        }
    }

    public LspPosition? TakePendingCaretTarget()
    {
        var target = _pendingCaretTarget;
        _pendingCaretTarget = null;
        return target;
    }

    [RelayCommand]
    private void RefreshTree()
    {
        // Rebuilding Root below creates an entirely new node tree, so any previously selected node
        // would otherwise be left pointing at a stale, now-orphaned instance.
        SelectedNode = null;

        if (string.IsNullOrWhiteSpace(WorkingDirectoryPath))
        {
            Root = null;
            StatusMessage = "Explorer_NoWorkingDirectory".Resolve();
            return;
        }

        if (!Directory.Exists(WorkingDirectoryPath))
        {
            Root = null;
            StatusMessage = string.Format("Explorer_WorkingDirectoryNotFound".Resolve(), WorkingDirectoryPath);
            return;
        }

        try
        {
            var touchedFullPaths = _promptResultStore.Files
                .Select(file => Path.GetFullPath(Path.Combine(WorkingDirectoryPath, file.FilePath)))
                .ToHashSet(StringComparer.Ordinal);

            Root = FileTreeNodeViewModel.FromEntry(FileTreeBuilder.Build(WorkingDirectoryPath), touchedFullPaths);
            StatusMessage = string.Empty;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read the working directory {WorkingDirectory} while building the Explorer tree.", WorkingDirectoryPath);
            Root = null;
            StatusMessage = string.Format("Explorer_LoadFailed".Resolve(), ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Failed to read the working directory {WorkingDirectory} while building the Explorer tree.", WorkingDirectoryPath);
            Root = null;
            StatusMessage = string.Format("Explorer_LoadFailed".Resolve(), ex.Message);
        }
    }
}
