using System.ComponentModel;

using FluentAvalonia.UI.Controls;

using Paarde.Avalonia.Strings;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.Models;
using Paarde.Core.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Paarde.Avalonia.ViewModels;

// Epic 23, T23.1.1: one instance per open Explorer tab, carrying everything that used to be a
// single slot (SelectedFilePath/SelectedFileContent/FileOverview/Diagnostics/etc.) on
// ExplorerViewModel itself. Owns Save/Revert/Explain/ghost-text/completion directly (same
// self-contained shape ProposedFileChangeViewModel already uses for a single file) since none of
// them need the file tree - only GoToDefinition/FindReferences stay on ExplorerViewModel, since
// jumping to another file means opening or activating a different tab.
public partial class OpenFileTabViewModel : ObservableObject
{
    private readonly string _workingDirectoryPath;
    private readonly IFileChangeApplyService _fileChangeApplyService;
    private readonly ILlmRunner _llmRunner;
    private readonly ILanguageServerSessionManager _languageServerSessionManager;
    private readonly ILogger<OpenFileTabViewModel> _logger;

    private string _originalContentOnDisk;
    private CancellationTokenSource? _ghostTextCancellationTokenSource;

    public string FilePath { get; }

    public string FileName => Path.GetFileName(FilePath);

    [ObservableProperty]
    private string content;

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity statusSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private string fileOverview = string.Empty;

    [ObservableProperty]
    private bool isLoadingOverview;

    [ObservableProperty]
    private IReadOnlyList<LspDiagnostic>? diagnostics;

    [ObservableProperty]
    private string? ghostTextSuggestion;

    [ObservableProperty]
    private IReadOnlyList<LspCompletionItem>? completionItems;

    [ObservableProperty]
    private LspDocumentSymbol? documentSymbols;

    [ObservableProperty]
    private LspHover? hoverSuggestion;

    [ObservableProperty]
    private ProposedFileChangeViewModel? pendingChange;

    // specs/editor-focus-layout R3: ExplorerViewModel is the one that owns PrimaryGroupTabs/
    // SecondaryGroupTabs and actually repositions this tab within whichever group it's in -
    // this just raises the event, same "tab raises, ExplorerViewModel reacts" split
    // ScrollToHunkRequested below already establishes.
    [ObservableProperty]
    private bool isPinned;

    public event Action? PinToggled;

    partial void OnIsPinnedChanged(bool value)
    {
        OnPropertyChanged(nameof(PinMenuItemLabel));
        PinToggled?.Invoke();
    }

    public string PinMenuItemLabel => IsPinned ? "Unpin" : "Pin";

    [RelayCommand]
    private void TogglePin() => IsPinned = !IsPinned;

    // specs/editor-focus-layout R2: 0 = primary group, 1 = secondary (split) group - at most 2
    // groups per the epic's non-goals, so this doesn't need to be an enum.
    [ObservableProperty]
    private int tabGroup;

    partial void OnTabGroupChanged(int value) => OnPropertyChanged(nameof(CanSplitRight));

    public bool CanSplitRight => TabGroup == 0;

    public event Action? SplitRightRequested;

    [RelayCommand(CanExecute = nameof(CanSplitRight))]
    private void RequestSplitRight() => SplitRightRequested?.Invoke();

    // Raised by TutorialStepViewModel's "View in Diff" action (via TutorialViewModel.FileFocusRequested)
    // once ExplorerViewModel has already opened/focused this tab - ExplorerFileTabControl.axaml.cs
    // subscribes to scroll the CodeEditor's caret to the hunk's SourceRangeStart, the same target
    // DiffsDetailControl.axaml.cs's ScrollToHunk already uses.
    public event Action<DiffHunk>? ScrollToHunkRequested;

    public void RequestScrollTo(DiffHunk hunk) => ScrollToHunkRequested?.Invoke(hunk);

    // Epic 25, T1: the view binds this instead of Content so a tab carrying a pending LLM proposal
    // edits the shared ProposedFileChangeViewModel (keeping Review's apply/refine flow and
    // Explorer's own tab in sync) while a plain, non-proposal tab keeps editing Content directly.
    public string EffectiveContent
    {
        get => PendingChange?.EditedContent ?? Content;
        set
        {
            if (PendingChange is { } change)
            {
                change.EditedContent = value;
            }
            else
            {
                Content = value;
            }
        }
    }

    partial void OnPendingChangeChanged(ProposedFileChangeViewModel? oldValue, ProposedFileChangeViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnPendingChangePropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnPendingChangePropertyChanged;
        }

        OnPropertyChanged(nameof(EffectiveContent));
    }

    private void OnPendingChangePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProposedFileChangeViewModel.EditedContent))
        {
            OnPropertyChanged(nameof(EffectiveContent));
        }
    }

    public OpenFileTabViewModel(
        string filePath,
        string initialContent,
        string workingDirectoryPath,
        IFileChangeApplyService fileChangeApplyService,
        ILlmRunner llmRunner,
        ILanguageServerSessionManager languageServerSessionManager,
        ILogger<OpenFileTabViewModel>? logger = null)
    {
        FilePath = filePath;
        content = initialContent;
        _originalContentOnDisk = initialContent;
        _workingDirectoryPath = workingDirectoryPath;
        _fileChangeApplyService = fileChangeApplyService;
        _llmRunner = llmRunner;
        _languageServerSessionManager = languageServerSessionManager;
        _logger = logger ?? NullLogger<OpenFileTabViewModel>.Instance;
    }

    partial void OnContentChanged(string value)
    {
        IsDirty = value != _originalContentOnDisk;
        _ = _languageServerSessionManager.NotifyDocumentChangedAsync(FilePath, value);
    }

    partial void OnIsDirtyChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        RevertToDiskCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingOverviewChanged(bool value) => ExplainCommand.NotifyCanExecuteChanged();

    // T10.2.2 (moved from ExplorerViewModel): writes through IFileChangeApplyService.WriteRaw for
    // the same path-containment check and encoding/line-ending preservation every other write in
    // this app already gets.
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var relativePath = Path.GetRelativePath(_workingDirectoryPath, FilePath).Replace(Path.DirectorySeparatorChar, '/');

        // T10.2.3: refuses outright rather than offering an "apply anyway" override, same scope
        // decision as before - this is a single ad-hoc edit, not the batch apply flow.
        if (ProtectedPathChecker.IsProtected(_workingDirectoryPath, relativePath))
        {
            StatusMessage = string.Format("Explorer_SaveBlockedProtectedPath".Resolve(), relativePath);
            StatusSeverity = InfoBarSeverity.Error;
            return;
        }

        // T10.2.4: refuses rather than silently clobbering if the file changed on disk since it
        // was opened here.
        var currentDiskContent = FileContentReader.ReadOrEmpty(FilePath);

        if (currentDiskContent != _originalContentOnDisk)
        {
            StatusMessage = string.Format("Explorer_SaveConflict".Resolve(), relativePath);
            StatusSeverity = InfoBarSeverity.Warning;
            return;
        }

        try
        {
            _fileChangeApplyService.WriteRaw(relativePath, Content, _workingDirectoryPath);
            _originalContentOnDisk = Content;
            IsDirty = false;
            StatusMessage = string.Format("Explorer_SaveSucceeded".Resolve(), relativePath);
            StatusSeverity = InfoBarSeverity.Success;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = string.Format("Explorer_SaveFailed".Resolve(), relativePath, ex.Message);
            StatusSeverity = InfoBarSeverity.Error;
        }
    }

    private bool CanSave() => IsDirty;

    [RelayCommand(CanExecute = nameof(CanRevertToDisk))]
    private void RevertToDisk()
    {
        _originalContentOnDisk = FileContentReader.ReadOrEmpty(FilePath);
        Content = _originalContentOnDisk;
    }

    private bool CanRevertToDisk() => IsDirty;

    [RelayCommand(CanExecute = nameof(CanExplain))]
    private async Task ExplainAsync()
    {
        IsLoadingOverview = true;
        FileOverview = string.Empty;

        try
        {
            var prompt =
                "Explain this file in a tutorial style - what it does, how it's structured, and " +
                "anything a newcomer to this codebase should notice. Respond in plain prose only - " +
                "no JSON, no markdown fences.\n\n" +
                $"File: {FilePath}\n\n{Content}";

            var result = await _llmRunner.RunPromptAsync(prompt, _workingDirectoryPath);

            FileOverview = result.Success
                ? result.Output.Trim()
                : string.Format("Explorer_ExplainFailed".Resolve(), result.ErrorMessage);
        }
        finally
        {
            IsLoadingOverview = false;
        }
    }

    private bool CanExplain() => !IsLoadingOverview;

    // Epic 15, T15.2 (moved from ExplorerViewModel): one cancellation token per tab now, rather
    // than one shared field - with several tabs editable at once, a ghost-text request in one
    // tab must not cancel an in-flight request in another.
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
                $"File: {FilePath}\n\n{textBeforeCaret}<cursor/>{textAfterCaret}";

            var result = await _llmRunner.RunPromptAsync(prompt, _workingDirectoryPath, cancellationToken: cancellationTokenSource.Token);
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

    public async Task RequestCompletionAsync(LspPosition position)
    {
        try
        {
            var items = await _languageServerSessionManager.GetCompletionAsync(FilePath, position);
            CompletionItems = null;
            CompletionItems = items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Completion request failed for {FilePath}.", FilePath);
        }
    }

    // specs/editor-intelligence R3: raised by CodeEditor.DocumentSymbolsRequested on file open and
    // after every settled edit (its own 400ms debounce) - same round-trip shape RequestCompletionAsync
    // above already uses.
    public async Task RequestDocumentSymbolsAsync()
    {
        try
        {
            DocumentSymbols = await _languageServerSessionManager.GetDocumentSymbolsAsync(FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Document symbols request failed for {FilePath}.", FilePath);
        }
    }

    // specs/editor-intelligence R6: raised by CodeEditor.HoverRequested once its own pointer-hover
    // debounce settles - same round-trip shape RequestCompletionAsync above already uses.
    public async Task RequestHoverAsync(LspPosition position)
    {
        try
        {
            var hover = await _languageServerSessionManager.GetHoverAsync(FilePath, position);
            HoverSuggestion = null;
            HoverSuggestion = hover;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hover request failed for {FilePath}.", FilePath);
        }
    }
}
