# Implementation tasks — Persistent workbench shell

Each task references the requirement(s) it satisfies.

- [x] T1 Add `IDiagnosticsAggregator`/`DiagnosticsAggregator` (`Paarde.Core/Contracts/Services/`,
  `Paarde.Core/Services/`) per design.md: subscribes directly to
  `ILanguageServerSessionManager.DiagnosticsPublished`, removes a file's entry on an empty publish,
  raises `Changed`. Register as a DI singleton. _(R4.1, R4.2, R4.3, R4.4)_
  _(Done 2026-10-27: added `IDiagnosticsAggregator`/`DiagnosticsAggregator`, registered as a
  singleton in `App.axaml.cs` right after `ILanguageServerSessionManager`. Verified with
  `DiagnosticsAggregatorTests` — 5 tests covering add, empty-publish removal, independent files,
  replace-not-append, and `Changed` firing exactly once per publish. `dotnet build Paarde.slnx`:
  0 warnings/0 errors.)_
- [x] T2 Extend `GitStatus`/`IGitStatusService.GetStatus` with `BranchName` (via
  `git branch --show-current`, null on detached HEAD or failure). Extend
  `ILanguageServerSessionManager` with `RunningLanguageIds`. _(R3.2, R3.4)_
  _(Done 2026-10-27: `GitStatus` gained a `BranchName` property (null for `NotARepository` and for
  detached HEAD); `GitStatusService.GetStatus` now also runs `git branch --show-current` with the
  same fail-open posture as the rest of the class. `ILanguageServerSessionManager.RunningLanguageIds`
  exposes a snapshot of the running server groups' keys. Verified with new
  `GitStatusServiceTests.GetStatus_RepositoryWithCommit_ReportsCurrentBranchName` (plus a
  `BranchName`-is-null assertion on the existing not-a-repository test) and new
  `LanguageServerSessionManagerTests.RunningLanguageIds_*` cases. Full solution test run:
  887/887 passed.)_
- [x] T3 Extend `CodeEditor` with a bindable `CaretPosition` (set from the existing
  `TextArea.Caret.PositionChanged` subscription). Add `IActiveEditorContext` (new, mirrors
  `IWorkingDirectoryContext`'s pub/sub shape) and wire it into every `CodeEditor` host
  (`ExplorerView.axaml.cs`, `TutorialDetailControl.axaml.cs`) via `GotFocus`/`LostFocus`/the new
  property's `Changed` observable. _(R3.5)_
  _(Done 2026-10-27: added `CodeEditor.CaretPositionProperty` (privately settable `StyledProperty<LspPosition?>`),
  set from the existing `TextArea.Caret.PositionChanged` handler alongside the bracket-highlight
  update already there. Added `IActiveEditorContext`/`ActiveEditorContext` (mirrors
  `IWorkingDirectoryContext` exactly), registered as a DI singleton. Wired both `ExplorerView` and
  `TutorialDetailControl` to forward `Editor.CaretPosition` via `Editor.PropertyChanged` while
  `Editor.IsFocused`, with `GotFocus`/`LostFocus` handling the initial push and the clear-on-blur
  case so the status bar won't show a stale position from whichever editor had focus last. Added
  `FakeActiveEditorContext` (`Paarde.Avalonia.Tests/Fakes/`) for the upcoming
  `StatusBarViewModelTests`. `dotnet build Paarde.slnx`: 0 warnings/0 errors. Full solution test
  run: 889/889 passed. No dedicated unit test for `CodeEditor.CaretPosition` itself -
  `CodeEditorCompletionDataTests` is the only existing precedent for testing this control and it
  only exercises a plain data-record helper, never `new CodeEditor()` directly, matching this
  repo's existing stance that AvaloniaEdit-control-level behavior is manual-verification-only in
  this sandboxed environment.)_
- [x] T4 Add `StatusBarViewModel` (DI singleton, owned by `ShellViewModel`) per design.md: git
  branch/dirty count, active `RunConfiguration` name, LSP running-languages summary, caret
  line:column. Add the status bar row to `ShellView.axaml`. _(R3.1–R3.5)_
  _(Done 2026-10-27: added `StatusBarViewModel` (DI singleton constructed as a `ShellViewModel`
  property, the same "singleton handed in, not created here" shape as `RunPanelViewModel`),
  subscribing to `IWorkingDirectoryContext.Changed` (git branch/dirty count via `IGitStatusService`,
  off the UI thread since `GetStatus` shells out to git), `RunPanelViewModel.PropertyChanged` on
  `SelectedConfiguration`, the new `ILanguageServerSessionManager.RunningLanguageIdsChanged` event
  (added alongside `DiagnosticsPublished` so a subscriber can re-read `RunningLanguageIds` on every
  actual state transition — required fixing `StopAllAsync` to early-return/skip raising the event
  when nothing was running, since `StartAllAsync` always calls it internally first), and
  `IActiveEditorContext.Changed` for the caret readout. Added the status bar row to `ShellView.axaml`
  below the existing Build/Run strip. Considered and reverted giving `ExplorerViewModel` a direct
  `StatusBarViewModel` dependency to force an eager LSP-status refresh — broke
  `ExplorerViewModelTests`' unrelated construction and re-coupled two singletons the codebase
  otherwise keeps decoupled via pub/sub (`IWorkingDirectoryContext`'s documented precedent); the new
  `RunningLanguageIdsChanged` event is the decoupled fix. Verified with `StatusBarViewModelTests`
  (11 tests — git status via the constructor's `LoadSavedWorkingDirectoryAsync` path rather than the
  live `Changed` event, since nothing pumps `Dispatcher.UIThread` in this plain xunit host, matching
  `RunPanelViewModelTests`' documented convention; `RefreshLanguageServerStatus()` exercised directly
  for the same reason; caret-position tests exercise the live `IActiveEditorContext.Changed` path
  directly since that handler never needs a `Dispatcher.Post` marshal — it's only ever raised from a
  `CodeEditor` host's own UI-thread event handlers). One git-status test was initially flaky — it
  awaited a fixed `Task.Yield()` count racing against `RefreshGitStatusAsync`'s genuine thread-pool
  hop (`Task.Run`); replaced with a polling `WaitUntilAsync` helper and confirmed stable across
  repeated runs. `dotnet build Paarde.slnx`: 0 warnings/0 errors. Full solution test run: 902/902
  passed.)_
- [x] T5 Split `RunPanelViewModel.OutputBuffer` into `BuildOutputBuffer`/`RunOutputBuffer`. Add
  `WorkbenchDockViewModel` (DI singleton, owned by `ShellViewModel`) wrapping `RunPanelViewModel`,
  a promoted-to-singleton `TerminalViewModel`, and `IDiagnosticsAggregator`. Replace `ShellView`'s
  single `Expander`/`TerminalControl` with the four-tab dock per design.md's layout. _(R1.1–R1.5)_
  _(Done 2026-10-27: `RunPanelViewModel.OutputBuffer` split into `BuildOutputBuffer`
  (`BuildInternalAsync`) and `RunOutputBuffer` (`RunAsync`/`OnProcessOutputReceived`); its
  `IsOutputExpanded` property was removed outright (superseded by `WorkbenchDockViewModel.
  IsExpanded`). Added `WorkbenchDockViewModel` (DI singleton, constructed as a `ShellViewModel`
  property): wraps `RunPanelViewModel`, `IDiagnosticsAggregator`, and `TerminalViewModel`
  (promoted `AddTransient` -> `AddSingleton` in `App.axaml.cs` so the Terminal page and the dock's
  Terminal tab share the exact same instance/session, per design.md's decision against two PTYs
  racing over one tab) — this made `TerminalViewModel.OnNavigatedFrom`'s old unsubscribe-from-the-
  PTY-manager body wrong (it would silently stop the dock tab's updates on every page navigation
  away from Terminal), so it's now a documented no-op. `ShellView.axaml`'s single
  `Expander`/`TerminalControl` row became a `TabControl` with Build/Run/Problems/Terminal
  `TabItem`s inside the same `Expander` (one shared `IsExpanded`, no per-tab expand/collapse state
  per design.md). `TabControl.SelectedItem` cannot bind directly against a string key (it binds
  the selected `TabItem` instance, not its `Header`) — confirmed by trying it first and hitting
  exactly the ambiguity design.md flagged — so `ShellView.axaml.cs` code-behind syncs
  `DockTabControl.SelectedIndex` <-> `WorkbenchDockViewModel.ActiveTabKey` both ways via a small
  literal index<->key table, the fallback design.md already anticipated. Problems tab: added
  `ProblemRow` (plain presentation record, `Paarde.Avalonia/ViewModels/`) and flattening logic in
  `ShellView.axaml.cs` reacting to `IDiagnosticsAggregator.Changed`, matching design.md's "not a
  new ViewModel-level collection type" decision. The old build-or-run auto-expand behavior
  (`RunPanelViewModel.IsOutputExpanded`) is now `WorkbenchDockViewModel` reacting to
  `RunPanelViewModel.PropertyChanged` on `IsBuilding`/`IsRunning` to set `IsExpanded = true` and
  switch `ActiveTabKey` to `"Build"`/`"Run"` respectively. `dotnet build Paarde.slnx`: 0
  warnings/0 errors.)_
- [x] T6 Dock persistence: `WorkbenchDockSettingsKeys`, restore/save `ActiveTabKey`/`IsExpanded`
  via `ILocalSettingsService`, same shape `ResizablePaneHelper` already established. _(R2.1, R2.2)_
  _(Done 2026-10-27: added `WorkbenchDockSettingsKeys` (`Dock.ActiveTab`, `Dock.IsExpanded`) as its
  own small key file, not folded into `WorkspaceLayoutSettingsKeys`/`RunConfigurationSettingsKeys`
  — a single app-wide value rather than a per-page or per-working-directory one, matching why
  `RunConfigurationSettingsKeys` already got its own file. Unlike `ResizablePaneHelper`'s
  explicit `OnSplitterDragCompleted()` trigger (no convenient change notification for a raw
  `GridLength`), `WorkbenchDockViewModel` uses `[ObservableProperty]`-generated
  `OnActiveTabKeyChanged`/`OnIsExpandedChanged` partial hooks to save automatically, restoring
  once via `ILocalSettingsService` in the constructor the same way `RunPanelViewModel`'s own
  `LoadSavedWorkingDirectoryAsync` already does.)_
- [x] T7 Tests: `DiagnosticsAggregatorTests`, `GitStatusServiceTests`/
  `LanguageServerSessionManagerTests` extensions, `WorkbenchDockViewModelTests`,
  `StatusBarViewModelTests` per design.md's "Testing approach". _(validates R1–R4)_
  _(Done 2026-10-27: `DiagnosticsAggregatorTests`/`GitStatusServiceTests`/
  `LanguageServerSessionManagerTests` extensions and `StatusBarViewModelTests` were already
  completed in T1–T4. Added `WorkbenchDockViewModelTests` (`Paarde.Avalonia.Tests`, 6 tests):
  `ActiveTabKey`/`IsExpanded` default to `"Run"`/`false` with no saved state, round-trip through a
  `FakeLocalSettingsService` (restored on construction, saved on change), and the auto-expand/
  switch-tab reaction to `RunPanelViewModel.IsBuilding`/`IsRunning` becoming true. Added
  `FakeDiagnosticsAggregator` (`Paarde.Avalonia.Tests/Fakes/`) for this and future dock-related
  tests. One xUnit analyzer warning caught at `dotnet build` time (xUnit1031: blocking `.Result`
  on a settings read inside a `[Fact]`) — fixed by making both affected tests `async Task` and
  awaiting the read directly instead. `dotnet build Paarde.slnx`: 0 warnings/0 errors. Full
  solution test run: 908/908 passed.)_
- [ ] T8 Manual verification: dock tabs switch and persist across restart; Build/Run keep
  independent output; Terminal tab and the Terminal page show the same live session; Problems
  lists diagnostics across files not just the one open in Explorer; status bar updates for
  branch/dirty count, configuration, LSP state, and caret position. Same sandboxed-environment
  limitation already documented for every other Avalonia-UI-touching task in this repo (no real
  display/window manager available here). _(validates R1–R4, R6.2-equivalent)_
