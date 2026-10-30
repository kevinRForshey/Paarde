# Implementation tasks — Command palette & Quick Open

Each task references the requirement(s) it satisfies.

- [x] T1 Add `ExplorerViewModel.OnNavigatedTo`'s `parameter is string filePath` branch (calls the
  existing private `OpenOrActivateTab`, guarded by `File.Exists`), with no other change to that
  method's existing working-directory-reload/`RefreshTree`/LSP-startup behavior. _(R1.4)_
  - Done. `Paarde.Avalonia/ViewModels/ExplorerViewModel.cs`'s `OnNavigatedTo`.
- [x] T2 Add `IDiagnosticsAggregator`-style new types `QuickOpenViewModel`
  (`Paarde.Avalonia/ViewModels/`) per design.md: `IsOpen`/`Query`/`Results`/`SelectedResult`,
  `OpenCommand`/`CloseCommand`/`ActivateSelectedCommand`, working-directory tracking via
  `ILocalSettingsService` + `IWorkingDirectoryContext.Changed` (same pattern as
  `StatusBarViewModel`), its own cached `FileTreeEntry` (not shared with `PromptViewModel`'s),
  filtering via the existing `FileMentionMatcher.FindMatches`. Register as a DI singleton. _(R1.1,
  R1.2, R1.3, R1.5)_
  - Done. `Paarde.Avalonia/ViewModels/QuickOpenViewModel.cs` (new). Also added
    `EmptyStateMessage` (R1.5) and `"QuickOpen_NoWorkingDirectory"` to `Strings/Resources.cs`.
- [x] T3 Add `CommandPaletteViewModel` (`Paarde.Avalonia/ViewModels/`) per design.md: the
  `PaletteCommand` record, the hand-written registry (Build/Run/Stop delegating to
  `RunPanelViewModel`'s existing commands, one "Go to `<Page>`" entry per
  `ShellView.axaml`-reachable page), `IsOpen`/`Query`/`Results`/`SelectedResult`,
  `OpenCommand`/`CloseCommand`/`ExecuteSelectedCommand`. Register as a DI singleton. _(R2.1, R2.2,
  R2.3, R2.4, R2.5)_
  - Done. `Paarde.Avalonia/ViewModels/CommandPaletteViewModel.cs` (new). `PaletteCommand` grew a
    computed `IsEnabled => CanExecute()` for R2.4's disabled-not-hidden XAML binding.
- [x] T4 Wire R3.2's mutual exclusion: `QuickOpenViewModel.Open()` sets
  `CommandPaletteViewModel.IsOpen = false` and vice versa (constructor reference between the two
  singletons, per design.md's reasoning for why this isn't a new pub/sub context type). _(R3.2)_
  - Done, with one deviation from design.md: true constructor injection between the two
    singletons would be a circular DI dependency the container can't resolve. Instead each gets a
    `AttachCommandPalette`/`AttachQuickOpen` setter, called once by `ShellViewModel`'s constructor
    after both are already resolved (T5) - same end result (a direct reference, not a new pub/sub
    type), without the circularity.
- [x] T5 Add `QuickOpenViewModel`/`CommandPaletteViewModel` as `ShellViewModel` properties (same
  "singleton handed in" shape as `RunPanelViewModel`/`StatusBarViewModel`/`WorkbenchDockViewModel`)
  and register both as DI singletons in `App.axaml.cs`. _(supports R1, R2, R3)_
  - Done. `ShellViewModel.cs`, `App.axaml.cs`.
- [x] T6 Add the two `Popup`s to `ShellView.axaml` per design.md's layout (anchored to
  `NavigationFrame`, each containing its own query `TextBox` + `ListBox`, `IsLightDismissEnabled`),
  plus `ShellView.axaml.cs`'s `KeyDown` handlers on each query `TextBox` (`Up`/`Down` move
  `SelectedResult`, `Enter` activates/executes, `Escape` closes) mirroring
  `PromptView.axaml.cs`'s existing `OnPromptTextBoxKeyDown` shape, and focus-the-query-box-on-open
  handling. _(R1.3, R2.5, R3.3)_
  - Done. `ShellView.axaml`, `ShellView.axaml.cs`.
- [x] T7 Add the two global `KeyBinding`s (`Ctrl+P`, `Ctrl+Shift+P`) in `ShellView.axaml.cs`'s
  `AttachedToVisualTree` handler per design.md, bound to each view model's `OpenCommand`. _(R1.1,
  R2.1, R3.1)_
  - Done. `ShellView.axaml.cs`'s `OnAttachedToVisualTree`.
- [x] T8 Tests: `QuickOpenViewModelTests`, `CommandPaletteViewModelTests`, `ExplorerViewModelTests`
  extensions for the new `OnNavigatedTo` branch (including the "non-`string`/`null` parameter keeps
  today's exact existing behavior" regression guard), per design.md's "Testing approach". _(validates
  R1, R2)_
  - Done. `dotnet build Paarde.slnx`: 0 warnings/0 errors. Full solution test run: 928/928 passed
    (401 Paarde.Avalonia.Tests + 527 Paarde.Core.Tests).
- [x] T9 Manual verification: `Ctrl+P`/`Ctrl+Shift+P` work from every page, not just Explorer;
  selecting a Quick Open result opens/activates the right Explorer tab without discarding other
  tabs' unsaved edits; disabled command-palette entries render visibly disabled and don't execute
  on `Enter`; opening one popup while the other is open closes the first. Same
  sandboxed-environment limitation already documented for every other Avalonia-UI-touching task in
  this repo (no real display/window manager available here). _(validates R1–R3)_
  - Deferred permanently - no display/window manager available in this sandboxed environment, same
    as every prior epic's equivalent manual-verification task.
