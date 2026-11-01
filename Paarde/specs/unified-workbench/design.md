# Design — Unified workbench (Assistant panel docked beside the editor)

## Summary of architectural change

Three pages (Main, Prompt, Tutorial) and a fourth, thinner one (Diffs) collapse into: Explorer
(unchanged page, now the startup page) plus a new docked **Assistant panel** living inside
`ExplorerView` itself, with two tabs — **Compose** (`PromptViewModel`, unchanged logic) and
**Review** (`TutorialViewModel`, unchanged apply/export/refine logic, minus its own editor/tab
strip). The only new "bridge" state is `OpenFileTabViewModel.PendingChange` — Explorer's own tabs
already are the one place a file is edited; this epic just lets a tab *also* carry the LLM's
proposed diff for that same file, instead of Tutorial keeping a second, parallel tab strip with its
own `CodeEditor` instance for the same content.

The key lifetime change: `PromptViewModel`/`TutorialViewModel`/the new `AssistantPanelViewModel`
move from `AddTransient` (page-scoped, recreated on every navigation) to `AddSingleton` — not
because anything here needs cross-page broadcasting (`IPromptResultStore` already does that job),
but because the panel must be reachable from `HistoryViewModel` (Fork/Rerun) without depending on
`ExplorerView` ever having been constructed, and a second accidental instance would mean "Fork" from
History silently updates a `PromptViewModel` nobody is looking at. This is the same rationale
`specs/command-palette` documents for why `TerminalViewModel` was promoted to a real `AddSingleton`
in Epic 22, not Explorer's "the Frame happens to cache the View" trick, which only works within a
single page's own round-trip.

`ExplorerViewModel` takes `AssistantPanelViewModel` as a constructor parameter (one direction:
`ExplorerViewModel` → `AssistantPanelViewModel` → `TutorialViewModel`/`PromptViewModel`). Review
needs to "reach into" Explorer two ways - raising `FileFocusRequested` (Explorer listens) *and*
reading `ExplorerViewModel.SelectedTab` (Review needs to see Explorer) - so, unlike the first draft
of this section assumed, `TutorialViewModel` *does* need a reference back to `ExplorerViewModel`,
which would be a real constructor-injection cycle
(`ExplorerViewModel → AssistantPanelViewModel → TutorialViewModel → ExplorerViewModel`) if taken as
a constructor parameter. This is exactly the shape Epic 24's `AttachX` pattern exists for: each
singleton is constructed independently (`TutorialViewModel` takes no `ExplorerViewModel` parameter
at all), and `ExplorerViewModel`'s own constructor calls a setter once both objects exist:

```csharp
public ExplorerViewModel(..., AssistantPanelViewModel assistantPanel, ...)
{
    AssistantPanel = assistantPanel;
    assistantPanel.Review.AttachExplorer(this);   // breaks the cycle, same shape as Epic 24's AttachCommandPalette/AttachQuickOpen
    _promptResultStore.Files.CollectionChanged += OnPromptResultFilesChanged;
    AssistantPanel.Review.FileFocusRequested += OnReviewFileFocusRequested;
}
```

`TutorialViewModel.AttachExplorer(ExplorerViewModel explorer)` stores the reference privately and
subscribes to `explorer.PropertyChanged` right there to re-raise `Selected`'s own
`PropertyChanged` when `explorer.SelectedTab` changes - see "New/changed types" #4 below.

## New/changed types

### 1. `AssistantPanelViewModel` (new, `Paarde.Avalonia/ViewModels/AssistantPanelViewModel.cs`)

```csharp
public partial class AssistantPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private int selectedTabIndex; // 0 = Compose, 1 = Review

    public PromptViewModel Compose { get; }

    public TutorialViewModel Review { get; }

    public AssistantPanelViewModel(PromptViewModel compose, TutorialViewModel review, IPromptResultStore promptResultStore)
    {
        Compose = compose;
        Review = review;

        // A finished run (successful or not - PromptResultStore.Tutorial is set either way)
        // should put the narrative in front of the user without them having to click anything.
        promptResultStore.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IPromptResultStore.Tutorial))
            {
                ShowReview();
            }
        };
    }

    public void ShowCompose() => SelectedTabIndex = 0;

    public void ShowReview() => SelectedTabIndex = 1;
}
```

Registered `AddSingleton` in `App.axaml.cs`, alongside promoting `PromptViewModel` and
`TutorialViewModel` from `AddTransient` to `AddSingleton`.

### 2. `OpenFileTabViewModel.PendingChange` (changed, `Paarde.Avalonia/ViewModels/OpenFileTabViewModel.cs`)

```csharp
[ObservableProperty]
private ProposedFileChangeViewModel? pendingChange;

// Raised by TutorialStepViewModel's "View in Diff" action (via TutorialViewModel.FileFocusRequested)
// once ExplorerViewModel has already opened/focused this tab - ExplorerFileTabControl.axaml.cs
// subscribes to scroll the CodeEditor's caret to the hunk's SourceRangeStart, the same target
// DiffsDetailControl.axaml.cs's ScrollToHunk already uses.
public event Action<DiffHunk>? ScrollToHunkRequested;

public void RequestScrollTo(DiffHunk hunk) => ScrollToHunkRequested?.Invoke(hunk);
```

`EffectiveContent` (new computed property the view binds instead of `Content`):

```csharp
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

partial void OnPendingChangeChanged(ProposedFileChangeViewModel? value) => OnPropertyChanged(nameof(EffectiveContent));
```

`PendingChange.EditedContent`'s own `PropertyChanged` is forwarded too (one extra subscribe/unsubscribe
pair in the `OnPendingChangeChanged` partial method), so edits made by Review's "Refine" /
"Apply" flow show up in Explorer's tab immediately, matching the "single source of truth" framing
`ProposedFileChangeViewModel` already documents.

### 3. `ExplorerViewModel` changes (`Paarde.Avalonia/ViewModels/ExplorerViewModel.cs`)

```csharp
public AssistantPanelViewModel AssistantPanel { get; }

public ExplorerViewModel(
    ILocalSettingsService localSettingsService,
    IPromptResultStore promptResultStore,
    ILlmRunner llmRunner,
    IFileChangeApplyService fileChangeApplyService,
    IDialogService dialogService,
    ILanguageServerSessionManager languageServerSessionManager,
    IFolderPickerService folderPickerService,          // new - R10
    AssistantPanelViewModel assistantPanel,              // new - R1/R2
    ILogger<ExplorerViewModel>? logger = null)
{
    // ...existing field assignments...
    AssistantPanel = assistantPanel;

    _promptResultStore.Files.CollectionChanged += OnPromptResultFilesChanged;          // R4
    AssistantPanel.Review.FileFocusRequested += OnReviewFileFocusRequested;            // R7
}

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

private void OnReviewFileFocusRequested(ProposedFileChangeViewModel change, DiffHunk? hunk)
{
    var tab = OpenOrActivateTab(ResolveFullPath(change.FilePath), change);
    if (hunk is not null)
    {
        tab.RequestScrollTo(hunk);
    }
}
```

`OpenOrActivateTab` grows an optional second parameter and now returns the tab (both are additive;
every existing call site - tree clicks, Quick Open, LSP navigation - keeps compiling unchanged by
relying on the default):

```csharp
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
    OpenTabs.Add(tab);
    SelectedTab = tab;
    _ = _languageServerSessionManager.NotifyDocumentOpenedAsync(filePath, content);
    return tab;
}
```

`ResolveFullPath` is the same relative-path-against-`WorkingDirectoryPath` join `RefreshTree()`'s
`touchedFullPaths` computation already does today - factored out so both call sites share it
instead of two copies of the same `Path.GetFullPath(Path.Combine(...))` logic.

New R10 command:

```csharp
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
```

### 4. `TutorialViewModel` changes (`Paarde.Avalonia/ViewModels/TutorialViewModel.cs`)

- Remove `INavigationAware`, `IConfirmNavigationAway`, `HasUnsavedChanges`, `OnNavigatedTo`,
  `OnNavigatedFrom` (R11).
- Remove `OpenTabs` (the `ProposedFileChangeViewModel` tab strip) and `CloseSelectedTabCommand`
  entirely (R6) - Explorer's own `OpenTabs`/`CloseSelectedTabAsync` (`OpenFileTabViewModel`-typed)
  is now the only tab strip for file content; Review no longer keeps a second one.
- `Selected` becomes a read of the Explorer tab currently focused, not its own settable backing
  field, via the `AttachExplorer` reference described above (no constructor parameter, no cycle):

  ```csharp
  private ExplorerViewModel? _explorer;

  public void AttachExplorer(ExplorerViewModel explorer)
  {
      _explorer = explorer;
      _explorer.PropertyChanged += (_, e) =>
      {
          if (e.PropertyName == nameof(ExplorerViewModel.SelectedTab))
          {
              OnPropertyChanged(nameof(Selected));
          }
      };
  }

  public ProposedFileChangeViewModel? Selected => _explorer?.SelectedTab?.PendingChange;
  ```
- New event, replacing `DiffJumpRequest`/`TutorialJumpRequest` navigation (R7):

  ```csharp
  public event Action<ProposedFileChangeViewModel, DiffHunk?>? FileFocusRequested;
  ```

  Raised by the file-list's selection-changed handler (`partial void OnSelectedFileListItemChanged`,
  new - replaces today's direct `OpenTabs.Add`/tab-switch logic) and by
  `TutorialStepViewModel`'s "View in Diff" action (passed `_file`/`Hunk.MatchedDiffHunks[0]`, same
  arguments `DiffJumpRequest` used to carry).
- Everything else (`ApplySelectedAsync`, `UndoLastApplyAsync`, `RevertAllAppliedAsync`,
  `RefineSelectedFileAsync`, `SendBuildTestErrorsToClaudeAsync`, `ReplaceFile`, the two export
  commands, the file list's tri-state selection, the apply-history log) is unchanged logic, just
  with `OpenTabs`-syncing removed from `ReplaceFile` (there is no second tab strip to keep in sync
  with any more).

### 5. `PromptViewModel` changes (`Paarde.Avalonia/ViewModels/PromptViewModel.cs`)

- Remove `INavigationAware`, `OnNavigatedTo`, `OnNavigatedFrom`.
- Replace the `OnNavigatedTo`'s `parameter is PromptLaunchRequest launchRequest` branch with a
  public method called directly by the new call sites (R8):

  ```csharp
  public void LoadLaunchRequest(PromptLaunchRequest request)
  {
      PromptText = request.PromptText;
      WorkingDirectoryPath = request.WorkingDirectoryPath;
      if (request.AutoRun)
      {
          RunPromptCommand.Execute(null);
      }
  }
  ```

  Everything else (`RunPromptAsync`, `@mention` completion, folder/file pickers) is unchanged.

### 6. View changes

- `ExplorerView.axaml`: third `Grid` column + `GridSplitter` + new
  `AssistantPanelControl.axaml` bound to `AssistantPanel`, collapse toggle button in the existing
  toolbar row, `ResizablePaneHelper` wired to the new `ExplorerAssistantPanelWidth` key (same
  pattern `ExplorerView.axaml.cs`'s constructor already uses for `ExplorerTreeWidth`). New
  "Open folder…" toolbar button bound to `OpenFolderCommand` (R10).
- `AssistantPanelControl.axaml` (new): a `TabControl`/`ui:TabView` with two items - "Compose"
  (hosts the content that is today `PromptView.axaml`'s body, moved into a new
  `ComposePanelControl.axaml` reusing the exact same markup) and "Review" (hosts a new
  `ReviewPanelControl.axaml`: the compact file list + apply/export/refine/history controls that
  are today `TutorialView.axaml`'s left sidebar, plus a content area showing the new
  `ReviewFileDetailControl.axaml` for `Selected`).
- `ReviewFileDetailControl.axaml` (new): `TutorialDetailControl.axaml` with its `CodeEditor` (and
  the header row that duplicated the file path Explorer's own tab header already shows) removed,
  keeping only the overview text and the `TutorialStepViewModel` step-card `ItemsControl`.
  `TutorialDetailControl.axaml[.cs]` is deleted once nothing references it.
- `ExplorerFileTabControl.axaml`: `CodeEditor` binds `helpers:TextEditorExtensions.BindableText="{Binding EffectiveContent, Mode=TwoWay}"` (was `Content`), plus `DiffLines="{Binding PendingChange.DiffLines}"` /
  `DiffHunks="{Binding PendingChange.DiffHunks}"`. A new, `PendingChange`-gated row (`IsVisible="{Binding PendingChange, Converter={x:Static ObjectConverters.IsNotNull}}"`) shows which file this is a proposed change for and a shortcut button to switch the Assistant panel to Review for it.
- `ExplorerFileTabControl.axaml.cs`: wires `Editor.DiffHunkActionRequested` to
  `PendingChange.SetDiffHunkSelection(...)`, the same handler shape
  `TutorialDetailControl.axaml.cs` already has, plus a new subscription to
  `ViewModel.ScrollToHunkRequested` that scrolls/positions the caret (same approach
  `DiffsDetailControl.axaml.cs`'s `ScrollToHunk` already uses, adapted from an `ItemsControl` row
  index to `CodeEditor`'s own `ScrollTo`/`TextArea.Caret.Line`).
- `ShellView.axaml`: remove the Main, Prompt, Diffs, Tutorial `NavigationViewItem`s. Explorer,
  Terminal, History, Settings remain.
- `App.axaml.cs`: startup navigation targets `typeof(ExplorerViewModel).FullName!`; DI
  registrations updated per the "New/changed types" section above (`AssistantPanelViewModel` new
  singleton; `PromptViewModel`/`TutorialViewModel` singleton instead of transient; `MainViewModel`/
  `DiffsViewModel` registrations removed).

### 7. Deleted types/files

- `Paarde.Avalonia/ViewModels/MainViewModel.cs`, `Views/MainView.axaml[.cs]`.
- `Paarde.Avalonia/ViewModels/DiffsViewModel.cs`, `Views/DiffsView.axaml[.cs]` (the page only -
  `Views/DiffsDetailControl.axaml[.cs]` is kept, still used by `HistoryView.axaml`).
- `Views/TutorialView.axaml[.cs]` (replaced by `ReviewPanelControl.axaml`), `TutorialDetailControl
  .axaml[.cs]` (replaced by `ReviewFileDetailControl.axaml`).
- The `DiffJumpRequest`/`TutorialJumpRequest` record types (wherever they're declared - grep to
  confirm no other usage before deleting, per R9).
- `WorkspaceLayoutSettingsKeys.DiffsFileListWidth`/`TutorialFileListWidth` (R12).

### 8. `HistoryViewModel` retarget (R8)

```csharp
public HistoryViewModel(IRunSessionStore runSessionStore, INavigationService navigationService, AssistantPanelViewModel assistantPanel)
{
    // ...
}

private void Launch(bool autoRun)
{
    if (Selected is not { } session)
    {
        return;
    }

    _assistantPanel.Compose.LoadLaunchRequest(new PromptLaunchRequest(session.Session.UserPromptText, session.Session.WorkingDirectoryPath, autoRun));
    _assistantPanel.ShowCompose();
    _navigationService.NavigateTo(typeof(ExplorerViewModel).FullName!);
}
```

## Testing approach

- `AssistantPanelViewModelTests` (new): `ShowCompose`/`ShowReview` toggle `SelectedTabIndex`;
  `IPromptResultStore.Tutorial` being set calls `ShowReview()` automatically.
- `ExplorerViewModelTests` extensions: `_promptResultStore.Files.Add(...)` opens/activates a tab
  with `PendingChange` set, without disturbing other open tabs' `IsDirty` content; re-adding a file
  whose tab is already open does not clobber an already-set `PendingChange`; `OpenFolderCommand`
  persists the setting and calls `RefreshTree()`; `FileFocusRequested` opens/focuses the right tab
  and forwards the hunk to `RequestScrollTo`.
- `OpenFileTabViewModelTests` extensions: `EffectiveContent` getter/setter routes through
  `PendingChange` when set, through `Content` when not; `PendingChange.EditedContent` changes raise
  `EffectiveContent`'s own `PropertyChanged`.
- `TutorialViewModelTests` updates: every existing apply/export/refine/history test keeps passing
  with `OpenTabs`-related assertions removed; new tests for `Selected` reading
  `ExplorerViewModel.SelectedTab?.PendingChange` and for `FileFocusRequested` firing with the right
  arguments from both the file list and a step card's "View in Diff".
- `PromptViewModelTests` updates: replace the old `OnNavigatedTo(PromptLaunchRequest)` test with a
  direct `LoadLaunchRequest(...)` test (same assertions, no navigation plumbing needed to exercise
  it).
- `HistoryViewModelTests`/(no `MainViewModelTests` survives - deleted with `MainViewModel`)
  updates: `Fork`/`Rerun` assert against `AssistantPanelViewModel.Compose`'s state and
  `NavigateTo(typeof(ExplorerViewModel)...)` instead of `typeof(PromptViewModel)...`.
- Full solution build + test run (`dotnet build Paarde.slnx`, full test suite) after each task
  group, matching every prior epic's convention in this repo.
- Manual verification of the rendered panel/layout is deferred permanently, same
  sandboxed-environment limitation documented for every other Avalonia-rendering task in this
  repo's `ROADMAP.md`.

## Open questions

None outstanding - the three questions `requirements.md` deferred to this document are resolved
above (`ReviewFileDetailControl` is `TutorialDetailControl` trimmed of its editor and duplicate
header, not a new shared sub-control; the Review→Explorer link is a one-directional event, not an
`AttachX` pair, since there's no actual cycle; `AssistantPanelViewModel` is an `ExplorerViewModel`
property, not a `ShellViewModel` one).
