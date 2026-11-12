# Implementation tasks — Interface mode (Editor-focused vs. Flow/Wizard)

Each task references the requirement(s) it satisfies.

## F26.1 — Setting + Settings page

- [x] T1 Add `InterfaceMode` enum (`Paarde.Avalonia/Models/`) and `InterfaceModeSettingsKeys.Mode`
  constant (`Paarde.Avalonia/Services/`). _(R1)_
- [x] T2 `SettingsViewModel`: add `InterfaceMode` `[ObservableProperty]` loaded from
  `ILocalSettingsService` in the constructor, `OnInterfaceModeChanged` partial method saving the
  new value and recomputing `InterfaceModeChangeRequiresRestart`. _(R1, R9)_
  Done: loaded in `OnNavigatedTo` (the `SelectedRunnerKind` pattern), not the constructor - the
  constructor runs before `ILocalSettingsService` is guaranteed initialized, same reason every
  other persisted `SettingsViewModel` property already defers its load to `OnNavigatedTo`.
- [x] T3 `SettingsView.axaml`: add the mode selector control + "takes effect after restart"
  caption, matching the `ThemeVariant` selector's existing visual pattern. _(R9)_
  Done: matched `SelectedRunnerKind`'s plain `IsChecked`+`EnumToBooleanConverter` RadioButton
  pattern instead (no live-apply `Command`, since a mode switch needs a restart either way).

## F26.2 — Startup and navigation wiring

- [x] T4 `App.axaml.cs`: read `InterfaceModeSettingsKeys.Mode` before the existing startup
  `NavigateTo` call and branch to `MainViewModel`/`ExplorerViewModel` accordingly. _(R2)_
- [x] T5 `ShellView.axaml.cs`: read the persisted mode in the constructor; when `FlowWizard`,
  insert Home/Compose/Review `NavigationViewItem`s ahead of the existing three. _(R3)_
  Done: read via a fire-and-forget async method called from the constructor (same shape
  `PromptViewModel.LoadSavedStateAsync` already uses), not a blocking read - avoids introducing
  sync-over-async. `NavigationViewService.GetSelectedItem` re-reads `MenuItems` live on every
  navigation, so the only effect of the async read resolving after the very first navigation is
  that one navigation's selected-item highlight, which self-corrects on the next.
- [x] T6 `PageService`: add `Configure<MainViewModel, MainView>()` and
  `Configure<TutorialViewModel, ReviewView>()`. `App.axaml.cs`: register both new views/view model
  with DI (`AddTransient<MainViewModel>()`/`AddTransient<MainView>()`,
  `AddTransient<ReviewView>()`). _(R4, R5, R6)_

## F26.3 — Revived/new pages

- [x] T7 `MainViewModel`/`MainView.axaml`: rebuild the pre-Epic-25 dashboard shape (recent
  runs/projects from `IRunSessionStore`, quick-prompt box) retargeted at the current singleton
  `PromptViewModel`'s page route. _(R5)_
  Done: deviated from requirements.md's "same as the original" - the original navigated with a
  `PromptLaunchRequest` nav parameter, which `PromptViewModel` can no longer receive since Epic 25
  removed its `INavigationAware` implementation (nav parameters only reach
  `INavigationAware.OnNavigatedTo`). `MainViewModel` now takes `PromptViewModel` as a direct
  constructor dependency and calls `LoadLaunchRequest(...)`/sets `WorkingDirectoryPath` directly
  before navigating with no parameter - the same direct-call pattern `HistoryViewModel.Launch`
  already uses for Fork/Rerun.
- [x] T8 `ReviewView.axaml`/`.axaml.cs`: thin wrapper resolving `TutorialViewModel` from DI and
  hosting `ReviewPanelControl` with that `DataContext`. _(R6)_
- [x] T9 `ExplorerView.axaml.cs`: mode-conditional default width (`0` when `FlowWizard`) passed to
  `AssistantPanelSplitter`'s `ResizablePaneHelper` construction. _(R7)_
  Done: deviated from design.md's synchronous-construction sketch - `_assistantPanelHelper` is now
  assigned later (inside an async `Loaded` handler, after the `InterfaceMode` read) instead of in
  the constructor, since `ResizablePaneHelper`'s `defaultWidth` is fixed at construction time and
  picking it requires an async settings read first. `OnToggleAssistantPanelButtonClick` and the
  splitter's `DragCompleted` handler null-guard the field for the brief window before it's set.
- [x] T10 `AssistantPanelViewModel`: gate the existing `IPromptResultStore.Tutorial`-changed
  subscription's side effect so it also calls `INavigationService.NavigateTo(typeof(
  TutorialViewModel).FullName!)` when `InterfaceMode` is `FlowWizard`, alongside the existing
  `ShowReview()` call (unconditional in both modes). _(R8)_

## F26.4 — Tests

- [x] T11 `InterfaceMode`/`InterfaceModeSettingsKeys` round-trip tests;
  `SettingsViewModelTests` additions for T2's persist-on-change and restart-flag behavior.
  _(validates R1)_
- [x] T12 `MainViewModelTests` (new): recent-runs/projects population, `OpenRecentRun`/
  `SendQuickPrompt` navigation targets. _(validates R5)_
- [x] T13 `AssistantPanelViewModelTests` extension: `Tutorial`-changed subscription calls
  `NavigateTo` only in `FlowWizard` mode. _(validates R8)_
- [x] T14 Manual verification: with `EditorFocused` (default/unset), app behavior is pixel-for-
  pixel identical to today's Epic 25 behavior. Switch to `FlowWizard` in Settings, restart, confirm
  Home → Compose → (run) → auto-navigate-to-Review, and confirm Explorer's Assistant panel starts
  collapsed but still opens correctly when dragged. Switch back, restart, confirm return to
  today's behavior. _(validates AC-1 through AC-5; same sandboxed-environment limitation as every
  other Avalonia-rendering manual-verification task in this roadmap - not runnable here)_
  Partly done on Windows in Paarde.Avalonia: switching to Flow/Wizard and restarting opens Home with
  Compose/Review in the nav; Explorer's Assistant panel starts collapsed and opens from its
  button; switching back and restarting returns to Explorer. Fixed along the way: opening Settings
  in Flow/Wizard mode silently saved Editor-focused again (and the LLM runner choice back to the
  Claude CLI) - `EnumToBooleanConverter.ConvertBack` wrote the *unchecked* radio's value back; and
  Home's "Getting started" steps ran off the window instead of wrapping. Still to do: Compose →
  (run) → auto-navigate-to-Review, which needs a real Claude run.
  2026-11-12: that last step failed in Paarde.WinUI - a real run finished (the CLI itself takes
  ~26s headless) but the app stayed on Compose. Cause: `AssistantPanelViewModel` owns the
  "run finished -> Review" subscription but, as a singleton, was only ever created by Explorer
  or History, so in Flow/Wizard mode (starting on Home) nothing was listening. Fixed in both
  apps by resolving it at startup, right after `RestoreLastSession`. Re-run confirmed by the user
  2026-11-12: Compose → (run) → Review now navigates automatically.
