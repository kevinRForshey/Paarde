# Implementation tasks — Unified workbench (Assistant panel docked beside the editor)

Each task references the requirement(s) it satisfies. Ordered so the solution builds and all
existing tests keep passing after every task - new/changed types before the views and DI/deletions
that depend on them, deletions last.

- [x] T1 Add `OpenFileTabViewModel.PendingChange` (`ProposedFileChangeViewModel?`,
  `[ObservableProperty]`), `EffectiveContent` (computed get/set per design.md, routing through
  `PendingChange.EditedContent` when set and `Content` otherwise), and
  `ScrollToHunkRequested`/`RequestScrollTo(DiffHunk)`. In `OnPendingChangeChanged`, subscribe/
  unsubscribe to the old/new `PendingChange.PropertyChanged` so an `EditedContent` change on the
  attached `ProposedFileChangeViewModel` raises `EffectiveContent`'s own `PropertyChanged`. _(R3)_
  — done 2026-10-31: implemented exactly per design.md #2, using the two-parameter
  `OnPendingChangeChanged(oldValue, newValue)` CommunityToolkit.Mvvm hook (8.4.2 supports it) to do
  the subscribe/unsubscribe cleanly. Added `OpenFileTabViewModelTests` coverage: `EffectiveContent`
  routing with/without a `PendingChange`, `PropertyChanged` forwarding when the attached
  `ProposedFileChangeViewModel.EditedContent` changes externally, and `RequestScrollTo` raising
  `ScrollToHunkRequested`. Solution builds with 0 warnings/0 errors; full suite (942 tests) passes.
- [x] T2 Add `AssistantPanelViewModel` (`Paarde.Avalonia/ViewModels/AssistantPanelViewModel.cs`)
  exactly per design.md #1: `SelectedTabIndex`, `Compose`/`Review` properties,
  `ShowCompose()`/`ShowReview()`, constructor subscription to
  `IPromptResultStore.PropertyChanged` (`Tutorial` property) calling `ShowReview()`. _(R1)_
  — done 2026-10-31: added verbatim per design.md #1. Not yet wired into DI (that's T6, which also
  promotes `PromptViewModel`/`TutorialViewModel` to `AddSingleton`) or consumed by any view. Added
  `AssistantPanelViewModelTests` (5 tests: `ShowCompose`/`ShowReview`, constructor exposes the given
  `Compose`/`Review` instances, auto-`ShowReview()` when `IPromptResultStore.Tutorial` changes, and
  a negative case confirming an unrelated store property change doesn't switch tabs) - used the real
  `PromptResultStore`, not `FakePromptResultStore`, since the fake intentionally raises
  `PropertyChanged` with a null `PropertyName` for its own bulk-invalidation callers, which wouldn't
  exercise the `nameof(IPromptResultStore.Tutorial)` filter this view model actually relies on in
  production. Solution builds with 0 warnings/0 errors; full suite (947 tests) passes.
- [x] T3 `PromptViewModel`: remove `INavigationAware` and `OnNavigatedTo`/`OnNavigatedFrom`; add
  public `LoadLaunchRequest(PromptLaunchRequest request)` with the same effect the removed
  `OnNavigatedTo` branch had (set `PromptText`/`WorkingDirectoryPath`, execute
  `RunPromptCommand` when `request.AutoRun`). _(R8)_
  — done 2026-10-31, with one addition beyond the literal task text: `OnNavigatedTo` also loaded
  saved settings (`PromptText`/`WorkingDirectoryPath`/`TimeoutSeconds`/`SelectedPreset`/
  `SelectedDepth`/`AttachedPaths`/`RecentWorkingDirectories`) every time the Prompt page was
  (re-)navigated to - once Compose is never navigated to at all (it's a docked panel on a
  singleton, per T2/T6), that loading has no event left to run on. Moved it verbatim into a new
  private `LoadSavedStateAsync()`, called once from the constructor, so this still happens exactly
  once at app startup instead of silently never happening again. `LoadLaunchRequest` added exactly
  per design.md, keeping the original's `RunPromptCommand.CanExecute(null)` guard (design.md's
  snippet omits it, but tasks.md's own wording says "same effect the removed `OnNavigatedTo`
  branch had," and the guard was there to avoid firing a run when nothing is actually runnable).
  Updated `PromptViewModelTests`: renamed the `OnNavigatedTo_*` settings-restoration tests to
  `Constructor_*` (dropped their now-redundant explicit trigger call), renamed the two
  launch-request tests to `LoadLaunchRequest_Fork_*`/`LoadLaunchRequest_Rerun_*`, and deleted
  `EstimatedTokenCountDisplay_DefaultsToEmpty` - it asserted the exact opposite of
  `Constructor_PopulatesEstimatedTokenCountDisplay` for the same post-construction moment, since
  the estimate is now always computed at startup rather than only after a page visit. Solution
  builds with 0 warnings/0 errors; full suite (946 tests) passes.
- [x] T4 `TutorialViewModel`:
  - Remove `INavigationAware`, `IConfirmNavigationAway`, `HasUnsavedChanges`, `OnNavigatedTo`,
    `OnNavigatedFrom`. _(R11)_
  - Remove `OpenTabs` and `CloseSelectedTabCommand`; remove the `OpenTabs`-syncing lines from
    `ReplaceFile`. _(R6)_
  - Add `private ExplorerViewModel? _explorer;` and `AttachExplorer(ExplorerViewModel explorer)`
    per design.md #4 (stores the reference, subscribes to `explorer.PropertyChanged` to re-raise
    `Selected`). Replace the old `[ObservableProperty] selected` field with the computed
    `Selected => _explorer?.SelectedTab?.PendingChange` property. _(R6)_
  - Add `public event Action<ProposedFileChangeViewModel, DiffHunk?>? FileFocusRequested;`. Add a
    new file-list selection-changed handler that raises it with a `null` hunk. _(R7)_
  - Update `TutorialStepViewModel`'s "View in Diff" action to raise
    `FileFocusRequested(_file, Hunk.MatchedDiffHunks[0])` (via a reference to the owning
    `TutorialViewModel`, same existing wiring shape `TutorialStepViewModel` already has to reach
    its parent) instead of `_navigationService.NavigateTo(typeof(DiffsViewModel).FullName!, new
    DiffJumpRequest(...))`. _(R7)_
  — done 2026-11-01, with a few additions/judgment calls beyond the literal task text:
  - `OnNavigatedTo`'s refresh logic (loading `Tutorial`/`Files` from the store, restoring the
    `StageAppliedFilesInGit` setting, refreshing apply-history entries) moved into the
    constructor, plus a live `_promptResultStore.PropertyChanged` subscription (filtered to
    `null`/`nameof(IPromptResultStore.Tutorial)`, matching the convention established in T2) so
    the view model stays current without a navigation hook to re-trigger it.
  - `FileFocusRequested` is a private-settable `event` field, so `TutorialStepViewModel` (a
    different class) can't invoke it directly - added a public `RequestFileFocus(file, hunk)`
    method on `TutorialViewModel` that does the `?.Invoke`, and `TutorialStepViewModel.ViewInDiff`
    calls that instead.
  - Added `SelectedFileListItem` (a plain settable `[ObservableProperty]`) to back the file
    list's `SelectedItem` two-way binding, with a `partial void OnSelectedFileListItemChanged`
    that calls `RequestFileFocus(value, null)` - this is the "new file-list selection-changed
    handler" the task describes; `Selected` itself stays read-only/computed.
  - Edited `TutorialView.axaml` ahead of its full T11 rework, only enough to keep the build green
    now that `Selected` is read-only and `OpenTabs`/`HasOpenTabs` are gone: the file list binds to
    `SelectedFileListItem` instead of `Selected`, and the old tab strip over `OpenTabs` was
    replaced with a plain `ContentControl` bound to `Selected`.
  - `TutorialDetailControl` needs the owning `TutorialViewModel` (for `TutorialStepViewModel`'s
    new `owner` constructor parameter) before it can build `Steps`, but `DataContextChanged` can
    fire before the control is attached to `TutorialView`'s visual tree, which is when
    `FindAncestorOfType<TutorialView>()` would otherwise fail on first materialization. Fixed by
    caching the resolved view model in a new `_owner` field, populated (and `RebuildSteps()`
    re-run) from a new `AttachedToVisualTree` handler rather than a per-call ancestor lookup.
  - `_pendingStepTarget`/`TakePendingStepTarget()` are left in place but are now dead code -
    nothing sets `_pendingStepTarget` anymore since the `OnNavigatedTo(TutorialJumpRequest)`
    branch that used to is gone. Left for T18 to delete alongside `TutorialJumpRequest` itself.
  - `TutorialViewModelTests`: replaced the `OnNavigatedTo_*`/`HasUnsavedChanges_*` tests with
    `Constructor_*` equivalents; added a `Select(sut, file)` test helper that stands in for
    Explorer selecting a file (constructs a real `ExplorerViewModel` with fakes for its own
    dependencies and assigns `SelectedTab` directly, bypassing disk I/O) and threaded it through
    the `RefineSelectedFile*`/`CanRefineSelectedFile_FileSelectedAndInstructionGiven_*` tests that
    depend on `Selected` being non-null; deleted the tab-management tests
    (`OnNavigatedTo_FilesPresent_OpensTheAutoSelectedFileAsATab`,
    `SelectingAnotherFile_OpensItAsAnAdditionalTabWithoutClosingTheOthers`,
    `ReselectingAnAlreadyOpenTab_DoesNotDuplicateIt`, `CanCloseSelectedTab_*`,
    `CloseSelectedTabCommand_*`, `RefineSelectedFileAsync_Success_SwapsTheRefinedFileIntoTheSameTabPosition`)
    and the `TutorialJumpRequest`-dependent tests
    (`OnNavigatedTo_TutorialJumpRequest_SelectsTheRequestedFileAndExposesAPendingStepTarget`,
    `TakePendingStepTarget_CalledTwice_OnlyReturnsTheTargetOnce`) outright, since that behavior
    either moved entirely into `ExplorerViewModel`'s scope (tab management - new coverage, if any,
    lands in T5) or became untestable dead code (pending T18's cleanup); also removed the
    now-invalid `Assert.Same(sut.Files[0], sut.Selected)` from
    `RefineSelectedFileAsync_Success_ReplacesTheFileInBothFilesAndThePromptResultStore`, since
    `ReplaceFile` no longer re-points any selection (that resync is `ExplorerViewModel`'s job, T5).
  - `TutorialStepViewModelTests`: `FakeNavigationService` replaced with a real `TutorialViewModel`
    (`_owner`) so tests can subscribe to its actual `FileFocusRequested` event; the old
    navigation-assertion test renamed to
    `ViewInDiff_HunkWithAMatchedDiffHunk_RaisesFileFocusRequestedOnTheOwnerWithThatHunk`.
  - `SelectionPersistenceTests`: dropped the now-nonexistent `tutorialViewModel.OnNavigatedTo(null!)`
    calls (the constructor already loads from the store eagerly).
  - Solution builds with 0 warnings/0 errors; full suite (934 tests) passes.
- [x] T5 `ExplorerViewModel`:
  - Add constructor parameters `IFolderPickerService folderPickerService` and
    `AssistantPanelViewModel assistantPanel`; expose `AssistantPanel { get; }`. In the
    constructor body: `assistantPanel.Review.AttachExplorer(this);`, subscribe
    `_promptResultStore.Files.CollectionChanged += OnPromptResultFilesChanged;`, subscribe
    `AssistantPanel.Review.FileFocusRequested += OnReviewFileFocusRequested;`. _(R1, R2)_
  - Factor the relative-path-to-full-path join `RefreshTree()`'s `touchedFullPaths` already
    computes into a shared private `ResolveFullPath(string relativePath)` helper; use it from both
    `RefreshTree()` and the two new handlers below. _(R4)_
  - Change `OpenOrActivateTab` to `private OpenFileTabViewModel OpenOrActivateTab(string filePath,
    ProposedFileChangeViewModel? pendingChange = null)` per design.md #3 (returns the tab;
    `existingTab.PendingChange ??= pendingChange` on the already-open-tab path; new tabs get
    `PendingChange = pendingChange`). Every existing call site (tree clicks, Quick Open's
    `OnNavigatedTo(string filePath)` branch, LSP go-to-definition/find-references) keeps compiling
    unchanged via the new parameter's default. _(R3, R4)_
  - Add `OnPromptResultFilesChanged(object?, NotifyCollectionChangedEventArgs)` per design.md #3
    (iterates `e.NewItems`, calls `OpenOrActivateTab(ResolveFullPath(change.FilePath), change)` for
    each). _(R4)_
  - Add `OnReviewFileFocusRequested(ProposedFileChangeViewModel change, DiffHunk? hunk)` per
    design.md #3 (calls `OpenOrActivateTab`, then `tab.RequestScrollTo(hunk)` if `hunk is not
    null`). _(R7)_
  - Add `[RelayCommand] OpenFolderAsync()` per design.md #3 (R10): picks a folder via
    `_folderPickerService`, persists `WorkingDirectorySettingsKeys.WorkingDirectory`, sets
    `WorkingDirectoryPath`, calls `RefreshTree()`.
  — done 2026-11-01, matching design.md #3 exactly (no deviations). Notes:
  - `ExplorerViewModel`'s constructor now calls `AssistantPanel.Review.AttachExplorer(this)` -
    `AssistantPanelViewModel`/`PromptViewModel`/`TutorialViewModel` are still registered
    `AddTransient`/not yet registered at all (`AssistantPanelViewModel`) in `App.axaml.cs`; wiring
    those registrations up (including `ExplorerViewModel` picking up its two new constructor
    parameters via DI) is T6, done next.
  - `ExplorerViewModelTests`/`TutorialViewModelTests`: both now need a real
    `AssistantPanelViewModel` to construct an `ExplorerViewModel` (its own `AttachExplorer`/
    `FileFocusRequested` wiring is exactly what these tests need, not just a constructor to
    satisfy) - `ExplorerViewModelTests` grew a `CreateAssistantPanel()` helper reusing the test
    class's own fakes/store; `TutorialViewModelTests` grew a `CreateStandaloneExplorer()` helper
    that builds its own throwaway store/fakes, since its `Select`/selection-cleared tests use the
    `ExplorerViewModel` purely as a stand-in for "Explorer selected a file" and don't exercise its
    `AssistantPanel` wiring at all.
  - Solution builds with 0 warnings/0 errors; full suite (934 tests) passes
    (`ClaudeCliRunnerTests.RunPromptAsync_AgentToolsEnabledAndCliRejectsTheFlag_ReturnsAnActionableErrorMessage`
    failed once under full-suite parallel execution with `Win32Exception: Text file busy` on a
    temp shell script shared across tests - a pre-existing flake unrelated to this task; passes
    cleanly in isolation).
- [x] T6 DI registrations in `App.axaml.cs`: add `AssistantPanelViewModel` as `AddSingleton`;
  change `PromptViewModel`/`TutorialViewModel` from `AddTransient` to `AddSingleton`; change
  `ExplorerViewModel`'s registration continues to resolve correctly once its constructor gains the
  two new parameters (DI resolves them automatically; no registration code change needed beyond
  the constructor signature itself). _(R1)_
  — done 2026-11-01, exactly as specified: `AssistantPanelViewModel` registered `AddSingleton`;
  `PromptViewModel`/`TutorialViewModel` changed from `AddTransient` to `AddSingleton`;
  `ExplorerViewModel` left `AddTransient` and untouched (its registration call needed no edit -
  DI resolves the two new constructor parameters, including the singleton `AssistantPanelViewModel`,
  automatically). `IFolderPickerService` was already registered (`AddTransient`, pre-existing for
  `PromptViewModel`), so no new registration was needed for that parameter either. Solution builds
  with 0 warnings/0 errors; full suite (934 tests) passes.
- [x] T7 `HistoryViewModel`: add `AssistantPanelViewModel` constructor parameter; change `Launch`
  to call `_assistantPanel.Compose.LoadLaunchRequest(...)` + `_assistantPanel.ShowCompose()` +
  `_navigationService.NavigateTo(typeof(ExplorerViewModel).FullName!)` instead of navigating to
  `typeof(PromptViewModel)`. _(R8)_ — done 2026-11-01, exactly as specified. Notes:
  - `PromptViewModel.LoadLaunchRequest(PromptLaunchRequest)` already existed (built ahead of
    schedule alongside an earlier task), so T7 only needed to change the caller.
  - `App.axaml.cs` needed no edit: `HistoryViewModel` is `AddTransient` and DI resolves the new
    `AssistantPanelViewModel` constructor parameter (singleton, since T6) automatically.
  - `HistoryViewModelTests`: added a `CreateAssistantPanel()` helper (same real
    `PromptViewModel`/`TutorialViewModel`-wrapping pattern as `AssistantPanelViewModelTests`) and
    rewrote `ForkCommand`/`RerunCommand` tests to assert on `assistantPanel.Compose.PromptText`/
    `WorkingDirectoryPath`/`SelectedTabIndex` and navigation to `ExplorerViewModel`, instead of
    the old `PromptLaunchRequest` navigation-parameter assertions (removed, now unreachable since
    `Launch` no longer passes a parameter to `NavigateTo`).
  - Solution builds with 0 warnings/0 errors; full suite (934 tests) passes.
- [x] T8 New `AssistantPanelControl.axaml[.cs]` (`Paarde.Avalonia/Views/`): a `ui:TabView`/
  `TabControl` with two items bound to `AssistantPanel.SelectedTabIndex`, "Compose" hosting a new
  `ComposePanelControl.axaml` and "Review" hosting a new `ReviewPanelControl.axaml`. _(R2)_ —
  done 2026-11-01. Notes:
  - Used a plain `TabControl` (not FluentAvalonia's `ui:TabView`) with two fixed `TabItem`s bound
    via `SelectedIndex="{Binding SelectedTabIndex, Mode=TwoWay}"` - matches the existing
    `TabControl` precedent already in `ExplorerView.axaml`/old `TutorialView.axaml`, and nothing
    else in the codebase uses `ui:TabView` yet.
  - Deviation (deliberate, to keep this task independently buildable): since `ComposePanelControl`
    and `ReviewPanelControl` don't exist until T9/T11, each `TabItem`'s content is a placeholder
    `TextBlock` for now; T9 and T11 will each replace their own tab's placeholder with the real
    control. Not referenced by any other view yet - `ExplorerView.axaml` wiring it in is T12.
  - Solution builds with 0 warnings/0 errors; full suite (934 tests) passes (unchanged - no
    existing test touches this new, unreferenced control).
- [x] T9 New `ComposePanelControl.axaml[.cs]`: move `PromptView.axaml`'s body (prompt textbox,
  `@mention` popup, folder/file pickers, run button/progress, streaming output) into this control
  unchanged, `DataContext`/`x:DataType` now `vm:PromptViewModel` bound via
  `AssistantPanelControl`'s `Compose` property instead of the control resolving its own view model
  from DI/navigation. _(R2)_ — done 2026-11-01, matching design.md exactly. Notes:
  - `ComposePanelControl.axaml`'s body is `PromptView.axaml`'s `Grid` copied verbatim (only
    `x:Class` changed). `PromptView.axaml[.cs]` itself is untouched for now - it's still the
    DI/navigation-resolving page the old `Prompt` nav item points at until T16/T17 remove that
    destination; deleting it isn't in scope until then.
  - `ComposePanelControl.axaml.cs` is `PromptView.axaml.cs`'s code-behind with one change: the old
    constructor-assigned `ViewModel { get; }` (resolved via `App.GetService<PromptViewModel>()`)
    became `private PromptViewModel ViewModel => (PromptViewModel)DataContext!;`, since this
    control's `DataContext` is now supplied externally (`AssistantPanelControl` binds it to
    `Compose`) rather than resolved by the control itself. Every `ViewModel.X` call site elsewhere
    in the file is unchanged.
  - `AssistantPanelControl.axaml`'s Compose `TabItem` placeholder (T8) is replaced with
    `<views:ComposePanelControl DataContext="{Binding Compose}" />`; the Review tab's placeholder
    is untouched (still T11).
  - Solution builds with 0 warnings/0 errors (only the pre-existing `NETSDK1187` locale warnings);
    full suite (934 tests) passes (unchanged - no existing test touches this new, still-unreferenced-
    by-any-navigated-view control; `AssistantPanelControl`/`ComposePanelControl` aren't exercised by
    ExplorerView until T12).
- [ ] T10 New `ReviewFileDetailControl.axaml[.cs]` (`Paarde.Avalonia/Views/`): copy
  `TutorialDetailControl.axaml`, delete its header row (duplicate file path - Explorer's tab header
  already shows it) and its `CodeEditor` + the `FlyoutBase.AttachedFlyout` references-list markup
  tied to it, keeping the overview `TextBlock`/`MarkdownTextBlock` and the `TutorialStepViewModel`
  step-card `ItemsControl` exactly as they are today. Code-behind trimmed to whatever (if anything)
  doesn't reference the removed editor. _(R6)_
- [ ] T11 New `ReviewPanelControl.axaml[.cs]` (`Paarde.Avalonia/Views/`): move `TutorialView.axaml`'s
  left sidebar (Apply/Export/Refine buttons, master file list with tri-state selection, apply-history
  log) and overview header into this control, with the per-file tabbed area (`TabControl` over
  `OpenTabs`) replaced by a single content area bound to `Selected` and hosting
  `ReviewFileDetailControl` (T10). The file list's `SelectedItem` binding drives
  `TutorialViewModel`'s new selection-changed handler (T4) instead of an `OpenTabs.Add`/tab-switch.
  `DataContext`/`x:DataType` is `vm:TutorialViewModel`, bound via `AssistantPanelControl`'s
  `Review` property. _(R6)_
- [ ] T12 `ExplorerView.axaml`: add the third `Grid` column + `GridSplitter` +
  `AssistantPanelControl` bound to `ExplorerViewModel.AssistantPanel`, plus a toolbar toggle button
  to collapse/expand it and a new "Open folder…" toolbar button bound to `OpenFolderCommand`.
  `ExplorerView.axaml.cs`'s constructor wires a second `ResizablePaneHelper` for the new
  `ExplorerAssistantPanelWidth` column, same shape as the existing `ExplorerTreeWidth` one. _(R2,
  R10)_
- [ ] T13 `ExplorerFileTabControl.axaml`: change
  `helpers:TextEditorExtensions.BindableText="{Binding Content, Mode=TwoWay}"` to
  `="{Binding EffectiveContent, Mode=TwoWay}"`; add `DiffLines="{Binding PendingChange.DiffLines}"`
  and `DiffHunks="{Binding PendingChange.DiffHunks}"`; add the `PendingChange`-gated row (file path
  + "Open in Review" button switching `AssistantPanel.SelectedTabIndex`) per design.md #6. _(R5)_
- [ ] T14 `ExplorerFileTabControl.axaml.cs`: wire `Editor.DiffHunkActionRequested` to
  `ViewModel.PendingChange?.SetDiffHunkSelection(...)` (same handler shape
  `TutorialDetailControl.axaml.cs` already has) and `ViewModel.ScrollToHunkRequested` to scroll the
  `CodeEditor` to the hunk's position (adapt `DiffsDetailControl.axaml.cs`'s `ScrollToHunk`
  approach from an `ItemsControl` row index to `CodeEditor`'s own scroll/caret API). _(R5, R7)_
- [ ] T15 `WorkspaceLayoutSettingsKeys`: add `ExplorerAssistantPanelWidth`; remove
  `DiffsFileListWidth`/`TutorialFileListWidth`. _(R12)_
- [ ] T16 `ShellView.axaml`: remove the Main, Prompt, Diffs, Tutorial `NavigationViewItem`s from
  `NavigationView.MenuItems`. `CommandPaletteViewModel`'s constructor: remove the `GoTo("Main",
  typeof(MainViewModel))`, `GoTo("Prompt", typeof(PromptViewModel))`, `GoTo("Diffs",
  typeof(DiffsViewModel))`, `GoTo("Tutorial", typeof(TutorialViewModel))` registry entries,
  leaving `Build`/`Run`/`Stop`/`Explorer`/`Terminal`/`History`/`Settings` untouched. _(R9, R13)_
- [ ] T17 `App.axaml.cs`: change the startup navigation call to
  `GetService<INavigationService>().NavigateTo(typeof(ExplorerViewModel).FullName!);`; remove the
  `MainViewModel`/`DiffsViewModel` DI registrations and their page-type mappings in `IPageService`'s
  setup. _(R10, R9)_
- [ ] T18 Grep the solution for `DiffJumpRequest`/`TutorialJumpRequest` to confirm T4/T16 removed
  every usage, then delete both record types' declarations. Delete
  `Paarde.Avalonia/ViewModels/DiffsViewModel.cs`, `Views/DiffsView.axaml[.cs]`,
  `Paarde.Avalonia/ViewModels/MainViewModel.cs`, `Views/MainView.axaml[.cs]`,
  `Views/TutorialView.axaml[.cs]`, `Views/TutorialDetailControl.axaml[.cs]`. Leave
  `Views/DiffsDetailControl.axaml[.cs]` in place (still used by `HistoryView.axaml`). _(R9, R6)_
- [ ] T19 Update/replace unit tests:
  - `OpenFileTabViewModelTests`: `EffectiveContent` routing (T1), `ScrollToHunkRequested` firing
    from `RequestScrollTo`.
  - New `AssistantPanelViewModelTests`: tab-switch helpers; auto-`ShowReview()` on
    `IPromptResultStore.Tutorial` changing.
  - `ExplorerViewModelTests` extensions: `_promptResultStore.Files.Add(...)` behavior (T5),
    `PendingChange ??=` not clobbering an existing value, `OpenFolderCommand`, `FileFocusRequested`
    → `RequestScrollTo` forwarding.
  - `TutorialViewModelTests`: remove every `OpenTabs`-dependent assertion; add tests for
    `AttachExplorer`/`Selected`/`FileFocusRequested` (T4).
  - `PromptViewModelTests`: replace the old `OnNavigatedTo(PromptLaunchRequest)` test with a direct
    `LoadLaunchRequest(...)` test (T3).
  - `HistoryViewModelTests`: update `Fork`/`Rerun` assertions to check
    `AssistantPanelViewModel.Compose` state and the new navigation target (T7).
  - Delete `MainViewModelTests`/`DiffsViewModelTests` (or whatever they're named) along with their
    subjects (T18).
  - `CommandPaletteViewModelTests`: update the fixed-registry-size/contents assertions for the
    four removed `GoTo` entries (T16).
  - `dotnet build Paarde.slnx`: 0 warnings/0 errors. Full solution test run passes. _(validates R1,
    R3, R4, R5, R6, R7, R8, R11, R13)_
- [ ] T20 Manual verification: Explorer is the first thing shown on launch; "Open folder…" works
  with no prior working directory set; running a prompt from the Compose tab auto-switches to
  Review and opens every result file as an Explorer tab with inline diff coloring and working
  gutter Accept/Reject buttons; editing a diffed tab's content keeps the diff markers in sync;
  "View in Diff" on a tutorial step focuses the right tab and scrolls to the right hunk; Fork/Rerun
  from History lands on Explorer with the Compose tab pre-filled; collapsing/resizing the Assistant
  panel persists across restarts. Same sandboxed-environment limitation already documented for
  every other Avalonia-rendering manual-verification task in this repo (no display/window manager
  available here) - deferred permanently, same as `specs/command-palette/tasks.md` T9. _(validates
  R1–R13)_
