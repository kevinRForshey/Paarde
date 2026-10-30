using System.Collections.ObjectModel;

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
        OpenTabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOpenTabs));
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

    private void OpenOrActivateTab(string filePath)
    {
        var existingTab = OpenTabs.FirstOrDefault(tab => string.Equals(tab.FilePath, filePath, StringComparison.Ordinal));

        if (existingTab is not null)
        {
            SelectedTab = existingTab;
            return;
        }

        var content = FileContentReader.ReadOrEmpty(filePath);
        var tab = new OpenFileTabViewModel(filePath, content, WorkingDirectoryPath, _fileChangeApplyService, _llmRunner, _languageServerSessionManager);
        OpenTabs.Add(tab);
        SelectedTab = tab;

        _ = _languageServerSessionManager.NotifyDocumentOpenedAsync(filePath, content);
    }

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

        // Epic 17, T17.2.1: tells the server this file is no longer open - a no-op if the server
        // isn't running for this language.
        _ = _languageServerSessionManager.NotifyDocumentClosedAsync(tab.FilePath);

        SelectedTab = OpenTabs.Count == 0 ? null : OpenTabs[Math.Min(index, OpenTabs.Count - 1)];
    }

    private bool CanCloseSelectedTab() => SelectedTab is not null;

    partial void OnSelectedTabChanged(OpenFileTabViewModel? value) => CloseSelectedTabCommand.NotifyCanExecuteChanged();

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
