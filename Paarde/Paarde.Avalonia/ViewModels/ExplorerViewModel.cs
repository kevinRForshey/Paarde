using System.Collections.ObjectModel;
using System.Collections.Specialized;

using Avalonia.Threading;

using FluentAvalonia.UI.Controls;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Contracts.ViewModels;
using Paarde.Avalonia.Services;
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
    private readonly IFolderPickerService _folderPickerService;
    private readonly ILogger<ExplorerViewModel> _logger;

    // Epic 25, T5: the docked Assistant panel - Review (TutorialViewModel) needs to see this
    // view model's SelectedTab and to push newly-proposed files into it as tabs, which is why
    // ExplorerViewModel owns the AttachExplorer/event wiring below rather than the reverse.
    public AssistantPanelViewModel AssistantPanel { get; }

    // Epic 17, T17.1.3: set by NavigateToLocation, consumed once by the active tab's
    // ExplorerFileTabControl.axaml.cs's Editor.TextChanged handler - same "take once, after the
    // target content has already loaded" pull pattern TutorialViewModel.TakePendingStepTarget()
    // already established, needed because a cross-file jump only reflects into Editor.Text after
    // the new tab's Content binding runs.
    private LspPosition? _pendingCaretTarget;

    [ObservableProperty]
    private string workingDirectoryPath = string.Empty;

    [ObservableProperty]
    private FileTreeNodeViewModel? root;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity statusSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private FileTreeNodeViewModel? selectedNode;

    // Epic 23, T23.1.1: every file opened from the tree this visit, each with its own content/
    // dirty/overview/diagnostics state (OpenFileTabViewModel) - replaces the single
    // SelectedFilePath/SelectedFileContent slot this page used to have, same OpenTabs/Selected
    // shape TutorialViewModel already established for its own tabbed detail pane.
    public ObservableCollection<OpenFileTabViewModel> OpenTabs { get; } = new();

    [ObservableProperty]
    private OpenFileTabViewModel? selectedTab;

    // Epic 23, T23.1.1: drives ExplorerView.axaml's choice between the tab strip and the "select a
    // file" placeholder, same OpenTabs.CollectionChanged -> HasOpenTabs shape TutorialViewModel
    // already uses for its own tab strip.
    public bool HasOpenTabs => OpenTabs.Count > 0;

    // specs/editor-focus-layout R1: ExplorerView.axaml.cs owns the live ColumnDefinitions this
    // needs to collapse/restore, so the command just raises an event rather than touching any
    // layout itself - same "ViewModel raises, View's code-behind that owns the live visual state
    // reacts" split OnToggleAssistantPanelButtonClick already established for a single pane.
    public event Action? ZenModeToggled;

    [RelayCommand]
    private void ToggleZenMode() => ZenModeToggled?.Invoke();

    // specs/editor-focus-layout R2: the active tab's "Split right" action moves it into this
    // second group; ExplorerView.axaml projects both onto their own TabView. Real, independently
    // ordered ObservableCollections (not a recomputed LINQ projection over OpenTabs) so each
    // TabView's native drag-reorder (CanReorderTabs) has a stable list to mutate directly.
    public ObservableCollection<OpenFileTabViewModel> PrimaryGroupTabs { get; } = new();

    public ObservableCollection<OpenFileTabViewModel> SecondaryGroupTabs { get; } = new();

    // Drives ExplorerView.axaml's split-group column collapsing back to width 0 once its last tab
    // closes - see design.md's "closing the secondary group's last tab collapses the column back
    // automatically."
    public bool HasSecondaryGroupTabs => SecondaryGroupTabs.Count > 0;

    // specs/editor-focus-layout R5: most-recently-activated order, updated on every SelectedTab
    // change below - separate from OpenTabs'/the group collections' own display order. Not an
    // ObservableCollection - nothing binds to it directly; MruSwitcherItems below is the live
    // snapshot the popup actually shows.
    private readonly List<OpenFileTabViewModel> _mruOrder = [];

    [ObservableProperty]
    private bool isMruSwitcherOpen;

    public ObservableCollection<OpenFileTabViewModel> MruSwitcherItems { get; } = new();

    [ObservableProperty]
    private OpenFileTabViewModel? mruSwitcherHighlighted;

    public ExplorerViewModel(
        ILocalSettingsService localSettingsService,
        IPromptResultStore promptResultStore,
        ILlmRunner llmRunner,
        IFileChangeApplyService fileChangeApplyService,
        IDialogService dialogService,
        ILanguageServerSessionManager languageServerSessionManager,
        IFolderPickerService folderPickerService,
        AssistantPanelViewModel assistantPanel,
        ILogger<ExplorerViewModel>? logger = null)
    {
        _promptResultStore = promptResultStore;
        _localSettingsService = localSettingsService;
        _llmRunner = llmRunner;
        _fileChangeApplyService = fileChangeApplyService;
        _dialogService = dialogService;
        _languageServerSessionManager = languageServerSessionManager;
        _folderPickerService = folderPickerService;
        _logger = logger ?? NullLogger<ExplorerViewModel>.Instance;

        AssistantPanel = assistantPanel;
        AssistantPanel.Review.AttachExplorer(this);
        _promptResultStore.Files.CollectionChanged += OnPromptResultFilesChanged;
        AssistantPanel.Review.FileFocusRequested += OnReviewFileFocusRequested;

        _languageServerSessionManager.DiagnosticsPublished += OnDiagnosticsPublished;
        OpenTabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOpenTabs));
        SecondaryGroupTabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSecondaryGroupTabs));
    }

    // T10.2.5: IConfirmNavigationAway - checked by NavigationService.OnNavigating before leaving
    // this page, same mechanism TutorialViewModel already uses for its own unsaved edits.
    public bool HasUnsavedChanges => OpenTabs.Any(tab => tab.IsDirty);

    // Epic 23, T23.1.1: selecting a tree node opens that file as a tab (or activates its existing
    // tab if it's already open) rather than replacing a single slot - VS Code's convention, and
    // the closer precedent to TutorialViewModel.OnSelectedChanged's "add to OpenTabs if not
    // already there" than inventing a double-click-only rule for this app's TreeView. Because
    // every open tab keeps its own content, switching the tree selection can no longer discard
    // anything - the T10.2.5 discard-confirmation this used to need is gone; closing a dirty tab
    // (CloseSelectedTabCommand below) is the one place that still needs to ask.
    partial void OnSelectedNodeChanged(FileTreeNodeViewModel? value)
    {
        if (value is null || value.IsDirectory)
        {
            return;
        }

        OpenOrActivateTab(value.FullPath);
    }

    // Epic 25, T5: grew a second, optional parameter (R3/R4) so a file newly proposed by a
    // prompt run (OnPromptResultFilesChanged) or re-focused from Review (OnReviewFileFocusRequested)
    // can carry its ProposedFileChangeViewModel straight into the tab it opens/activates, while
    // every pre-existing call site (tree clicks, Quick Open, LSP navigation) keeps compiling
    // unchanged via the default. An already-open tab keeps whichever PendingChange it already had
    // rather than letting a later, unrelated open clobber it.
    private OpenFileTabViewModel OpenOrActivateTab(string filePath, ProposedFileChangeViewModel? pendingChange = null)
    {
        var existingTab = OpenTabs.FirstOrDefault(tab => string.Equals(tab.FilePath, filePath, StringComparison.Ordinal));

        if (existingTab is not null)
        {
            existingTab.PendingChange ??= pendingChange;
            SelectedTab = existingTab;
            return existingTab;
        }

        var content = FileContentReader.ReadOrEmpty(filePath);
        var tab = new OpenFileTabViewModel(filePath, content, WorkingDirectoryPath, _fileChangeApplyService, _llmRunner, _languageServerSessionManager)
        {
            PendingChange = pendingChange,
        };

        // specs/editor-focus-layout R2/R3: OpenFileTabViewModel raises these for the pin/split
        // actions it exposes on itself (same "tab raises, ExplorerViewModel reacts" shape
        // ScrollToHunkRequested already uses) - ExplorerViewModel is the one that owns the group
        // collections these actually mutate.
        tab.PinToggled += () => OnTabPinToggled(tab);
        tab.SplitRightRequested += () => OnTabSplitRightRequested(tab);

        OpenTabs.Add(tab);
        PrimaryGroupTabs.Add(tab);
        SelectedTab = tab;

        _ = _languageServerSessionManager.NotifyDocumentOpenedAsync(filePath, content);
        return tab;
    }

    // Epic 25, T5 (R4): a prompt run just added file(s) to IPromptResultStore.Files - open/activate
    // a tab for each one carrying its proposed change, mirroring what used to be Review's own
    // separate tab strip.
    private void OnPromptResultFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null)
        {
            return;
        }

        foreach (ProposedFileChangeViewModel change in e.NewItems)
        {
            OpenOrActivateTab(ResolveFullPath(change.FilePath), change);
        }
    }

    // Epic 25, T5 (R7): Review's file list / a step card's "View in Diff" asks to focus a
    // proposed change - open/activate its tab and, if a specific hunk was targeted, ask the tab
    // to scroll its editor there once it's loaded.
    private void OnReviewFileFocusRequested(ProposedFileChangeViewModel change, DiffHunk? hunk)
    {
        var tab = OpenOrActivateTab(ResolveFullPath(change.FilePath), change);
        if (hunk is not null)
        {
            tab.RequestScrollTo(hunk);
        }
    }

    // Epic 25, T5 (R4): factored out of RefreshTree's touchedFullPaths computation so both it and
    // the two handlers above share the same relative-path-against-WorkingDirectoryPath join.
    private string ResolveFullPath(string relativePath) => Path.GetFullPath(Path.Combine(WorkingDirectoryPath, relativePath));

    // Epic 23, T23.1.2: close-selects-neighbor, same shape as TutorialViewModel.CloseSelectedTab -
    // confirms first if the tab being closed has unsaved edits, since closing (unlike switching
    // tabs now) really does discard them.
    [RelayCommand(CanExecute = nameof(CanCloseSelectedTab))]
    private async Task CloseSelectedTabAsync()
    {
        if (SelectedTab is not { } tab)
        {
            return;
        }

        if (tab.IsDirty)
        {
            var discard = await _dialogService.ConfirmAsync(
                "Explorer_CloseDirtyTabConfirmTitle".Resolve(),
                "Explorer_CloseDirtyTabConfirmMessage".Resolve(),
                "Explorer_CloseDirtyTabConfirmButton".Resolve(),
                "Explorer_CloseDirtyTabCancelButton".Resolve());

            if (!discard)
            {
                return;
            }
        }

        var index = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);
        GroupCollectionFor(tab).Remove(tab);
        _mruOrder.Remove(tab);

        // Epic 17, T17.2.1: tells the server this file is no longer open - a no-op if the server
        // isn't running for this language.
        _ = _languageServerSessionManager.NotifyDocumentClosedAsync(tab.FilePath);

        SelectedTab = OpenTabs.Count == 0 ? null : OpenTabs[Math.Min(index, OpenTabs.Count - 1)];
    }

    private bool CanCloseSelectedTab() => SelectedTab is not null;

    // specs/editor-focus-layout R5: moves the newly-activated tab to the front of the MRU order -
    // a plain List<T>, not an ObservableCollection, since nothing binds to it directly (only the
    // Ctrl+Tab popup's own snapshot, MruSwitcherItems, does).
    partial void OnSelectedTabChanged(OpenFileTabViewModel? value)
    {
        CloseSelectedTabCommand.NotifyCanExecuteChanged();

        if (value is not null)
        {
            _mruOrder.Remove(value);
            _mruOrder.Insert(0, value);
        }
    }

    private ObservableCollection<OpenFileTabViewModel> GroupCollectionFor(OpenFileTabViewModel tab) =>
        tab.TabGroup == 1 ? SecondaryGroupTabs : PrimaryGroupTabs;

    // specs/editor-focus-layout R3: pinning clusters a tab with every other already-pinned tab in
    // its own group, right after the last of them - unpinning leaves its position untouched (only
    // pinning's effect on ordering is specified/tested).
    private void OnTabPinToggled(OpenFileTabViewModel tab)
    {
        if (!tab.IsPinned)
        {
            return;
        }

        var group = GroupCollectionFor(tab);
        var currentIndex = group.IndexOf(tab);
        if (currentIndex < 0)
        {
            return;
        }

        var targetIndex = group.Count(t => t.IsPinned) - 1;
        if (targetIndex != currentIndex)
        {
            group.Move(currentIndex, targetIndex);
        }
    }

    // specs/editor-focus-layout R2: "split right" is a one-way move into the second TabView's
    // group - matches the "split view, not tiling" non-goal, which never asks for a tab to move
    // back out of the secondary group.
    private void OnTabSplitRightRequested(OpenFileTabViewModel tab)
    {
        if (tab.TabGroup == 1)
        {
            return;
        }

        PrimaryGroupTabs.Remove(tab);
        tab.TabGroup = 1;
        SecondaryGroupTabs.Add(tab);
    }

    // specs/editor-focus-layout R5: opens the Ctrl+Tab popup - ExplorerView.axaml.cs calls this on
    // the first Ctrl+Tab keydown, then AdvanceMruSwitcher on every repeated Tab while Ctrl stays
    // held. Highlights the second entry (the previously-active tab) rather than the first (the
    // still-current tab), matching every mainstream editor's own "Ctrl+Tab jumps to the previous
    // tab on the very first press" convention.
    public void OpenMruSwitcher()
    {
        if (_mruOrder.Count == 0)
        {
            return;
        }

        MruSwitcherItems.Clear();
        foreach (var tab in _mruOrder)
        {
            MruSwitcherItems.Add(tab);
        }

        MruSwitcherHighlighted = MruSwitcherItems.Count > 1 ? MruSwitcherItems[1] : MruSwitcherItems[0];
        IsMruSwitcherOpen = true;
    }

    public void AdvanceMruSwitcher()
    {
        if (MruSwitcherItems.Count == 0)
        {
            return;
        }

        var currentIndex = MruSwitcherHighlighted is null ? -1 : MruSwitcherItems.IndexOf(MruSwitcherHighlighted);
        MruSwitcherHighlighted = MruSwitcherItems[(currentIndex + 1) % MruSwitcherItems.Count];
    }

    public void CommitMruSwitcher()
    {
        if (MruSwitcherHighlighted is not null)
        {
            SelectedTab = MruSwitcherHighlighted;
        }

        IsMruSwitcherOpen = false;
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

            // Epic 24, R1.4: Quick Open navigates here with the absolute path of the file to open.
            if (parameter is string filePath && File.Exists(filePath))
            {
                OpenOrActivateTab(filePath);
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
    // already carry. Epic 23: routed to whichever open tab matches filePath (there can be several
    // now) rather than a single active-file slot; a push for a file that isn't open as a tab is
    // silently dropped, same as before.
    private void OnDiagnosticsPublished(string filePath, IReadOnlyList<LspDiagnostic> publishedDiagnostics) =>
        Dispatcher.UIThread.Post(() =>
        {
            var tab = OpenTabs.FirstOrDefault(t => string.Equals(t.FilePath, filePath, StringComparison.Ordinal));
            if (tab is not null)
            {
                tab.Diagnostics = publishedDiagnostics;
            }
        });

    // Epic 17, T17.1.3: go-to-definition - navigates straight to the first result, same "don't
    // build a disambiguation UI for the common single-result case" scope decision FindReferencesAsync
    // below explains for its own (genuinely multi-result) case. Epic 23: takes the requesting
    // tab's own FilePath explicitly, since there's no longer a single SelectedFilePath to assume.
    public async Task GoToDefinitionAsync(string filePath, LspPosition position)
    {
        try
        {
            var locations = await _languageServerSessionManager.GetDefinitionAsync(filePath, position);
            if (locations.Count > 0)
            {
                NavigateToLocation(locations[0]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Go-to-definition failed for {FilePath}.", filePath);
        }
    }

    // Epic 17, T17.1.3: find-references - returns every result for ExplorerView's minimal flyout to
    // list, rather than jumping straight to one, since references (unlike a definition) are
    // routinely more than one and there's no single "right" result to prefer.
    public async Task<IReadOnlyList<LspLocation>> FindReferencesAsync(string filePath, LspPosition position)
    {
        try
        {
            return await _languageServerSessionManager.GetReferencesAsync(filePath, position);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Find-references failed for {FilePath}.", filePath);
            return Array.Empty<LspLocation>();
        }
    }

    // Epic 17, T17.1.3: jumps to location - opens or activates a tab for the target file (Epic 23:
    // the same open-or-activate path tree selection uses) before leaving the caret position for
    // ExplorerView to apply via TakePendingCaretTarget.
    public void NavigateToLocation(LspLocation location)
    {
        _pendingCaretTarget = location.Position;
        OpenOrActivateTab(location.FilePath);

        // Keeps the tree's highlighted node in sync with the tab a cross-file jump just
        // activated - OpenOrActivateTab above already did the actual opening, so this is purely
        // cosmetic and safe to skip if the target isn't in the currently-loaded tree.
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
            StatusSeverity = InfoBarSeverity.Warning;
            return;
        }

        if (!Directory.Exists(WorkingDirectoryPath))
        {
            Root = null;
            StatusMessage = string.Format("Explorer_WorkingDirectoryNotFound".Resolve(), WorkingDirectoryPath);
            StatusSeverity = InfoBarSeverity.Error;
            return;
        }

        try
        {
            var touchedFullPaths = _promptResultStore.Files
                .Select(file => ResolveFullPath(file.FilePath))
                .ToHashSet(StringComparer.Ordinal);

            Root = FileTreeNodeViewModel.FromEntry(FileTreeBuilder.Build(WorkingDirectoryPath), touchedFullPaths);
            StatusMessage = string.Empty;
            StatusSeverity = InfoBarSeverity.Informational;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read the working directory {WorkingDirectory} while building the Explorer tree.", WorkingDirectoryPath);
            Root = null;
            StatusMessage = string.Format("Explorer_LoadFailed".Resolve(), ex.Message);
            StatusSeverity = InfoBarSeverity.Error;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Failed to read the working directory {WorkingDirectory} while building the Explorer tree.", WorkingDirectoryPath);
            Root = null;
            StatusMessage = string.Format("Explorer_LoadFailed".Resolve(), ex.Message);
            StatusSeverity = InfoBarSeverity.Error;
        }
    }

    // Epic 25, T5 (R10): lets the user pick a working directory from the Explorer page itself,
    // rather than only ever inheriting whichever one Compose/Prompt last saved.
    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var path = await _folderPickerService.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await _localSettingsService.SaveSettingAsync(WorkingDirectorySettingsKeys.WorkingDirectory, path);
        WorkingDirectoryPath = path;
        RefreshTree();
    }
}
