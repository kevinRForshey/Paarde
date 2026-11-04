# Requirements — Interface mode (Editor-focused vs. Flow/Wizard)

## Status

Draft. Not started. This is Epic 26 (`ROADMAP.md`) — referenced elsewhere in this repo as
`[[interface-mode]]`. Sequenced immediately after Epic 25 and *before* Epics 27–28
(`[[editor-intelligence]]`, `[[editor-focus-layout]]`), since those two add depth/layout
affordances to Editor-focused mode specifically and should be built against a shell that already
knows a second mode exists, rather than retrofitted later.

## Context and current state

Confirmed by direct inspection, 2026-11-03:

- `App.axaml.cs` hardcodes the startup page to `ExplorerViewModel` (retargeted by Epic 25's T20 -
  previously `MainViewModel`). `ShellView.axaml`'s `NavigationView.MenuItems` statically declares
  three items - Explorer, Terminal, History - plus Settings via `IsSettingsVisible="True"` (not a
  `MenuItems` entry).
- `AssistantPanelViewModel` (`AddSingleton`) composes `Compose` (`PromptViewModel`) and `Review`
  (`TutorialViewModel`), both themselves promoted to `AddSingleton` by Epic 25, as a two-tab docked
  panel living inside `ExplorerView` via `AssistantPanelControl`. It subscribes to
  `IPromptResultStore.PropertyChanged` (the `Tutorial` property) to flip `SelectedTabIndex` to the
  Review tab the moment a run finishes.
- `Views/PromptView.axaml`/`.axaml.cs` **still exists, fully functional, and is still registered**
  - `services.AddTransient<PromptView>()` in `App.axaml.cs`, `Configure<PromptViewModel,
  PromptView>()` in `PageService` - but is orphaned: no `NavigationView` item points to it since
  Epic 25's T9 moved its body into `ComposePanelControl` (confirmed by reading both files side by
  side; `ComposePanelControl`'s own code comment says "`PromptView.axaml`'s body, moved here
  unchanged", but `PromptView.axaml` itself was never deleted or emptied). Its code-behind resolves
  `PromptViewModel` the normal navigated-page way (DI, not DataContext-supplied-externally), and
  since `PromptViewModel` is now a singleton, navigating to it today would show the exact same live
  state the docked Compose tab shows.
- `ComposePanelControl`/`ReviewPanelControl`/`ReviewFileDetailControl` (Epic 25) are all written to
  take their `DataContext` **externally** (confirmed via each file's own code comment, e.g.
  `ReviewPanelControl.axaml.cs`: "unlike `TutorialView` ..., this control's `DataContext` is
  supplied externally ... so there's no `ViewModel` property/`DataContext` assignment here") - a
  deliberate Epic 25 design choice that makes them reusable as either docked-panel content or
  standalone-page content with zero changes.
- No `TutorialViewModel` page mapping exists in `PageService` today (`TutorialView` was deleted by
  Epic 25). No `MainViewModel`/`MainView` exist today (deleted by Epic 25, commit `5072a09`,
  "T18: cleanup of the tutorial detail control"). The pre-deletion `MainViewModel` (recovered via
  `git show 5072a09^:.../MainViewModel.cs`) was a landing-page dashboard: `RecentRuns`/
  `RecentProjects` read fresh from `IRunSessionStore` on every `OnNavigatedTo`, plus
  `QuickPromptText`, with `OpenRecentRun`/`OpenRecentProject`/`SendQuickPrompt` all handing off to
  the Prompt page via `INavigationService.NavigateTo(typeof(PromptViewModel).FullName!, ...)` with
  a `PromptLaunchRequest`.
- `ILocalSettingsService`/`WorkspaceLayoutSettingsKeys` already establish the persisted-setting
  convention this epic's own new setting will follow. `SettingsViewModel.ThemeVariant` +
  `SwitchThemeCommand` is the existing precedent for an enum-like setting with its own dedicated
  apply command. `specs/multi-language-support/requirements.md` already established a "restart
  required, not hot-swapped" precedent for a setting that changes app wiring established at
  startup (per-language LSP server config) - this epic's `InterfaceMode` setting is the same kind
  of startup-time wiring decision.
- `ExplorerView.axaml.cs`'s `ResizablePaneHelper` already treats a pane width of `0` as
  "collapsed" for both `TreeSplitter` and `AssistantPanelSplitter`, and `ToggleAssistantPanelButton`
  already drives this programmatically for the Assistant panel alone.

## Goals

- Let the user choose, from Settings, between two interface modes - **without** maintaining two
  divergent implementations of the compose/review business logic or its markup:
  - **Editor-focused** (today's Epic 25 behavior; default). Explorer is the landing page; the
    editor plus a docked Assistant panel (Compose/Review tabs) is the whole app.
  - **Flow/Wizard**. A guided, step-by-step page flow - Home (dashboard) → Compose (full-page
    prompt) → Review (full-page narrative/apply) - reusing the *exact same* `PromptViewModel`/
    `TutorialViewModel` singleton instances Editor-focused mode already uses, exposed as separate
    `NavigationView` destinations instead of docked tabs.
- Zero duplication: Flow/Wizard's Compose/Review pages host the same `PromptView`/new
  `ReviewView` wrappers around `ComposePanelControl`/`ReviewPanelControl`'s existing,
  DataContext-supplied-externally markup - not a second implementation of either page.
- Changing the mode takes effect on the next app restart (same precedent
  `[[multi-language-support]]` already set), not hot-swapped mid-session.

## Non-goals

- No change to `PromptViewModel`/`TutorialViewModel`/`AssistantPanelViewModel`'s own business
  logic - this epic is purely about which pages/menu items expose them and which page the app
  lands on.
- No removal of Editor-focused mode's docked Assistant panel when Flow/Wizard mode is selected -
  it stays reachable (just collapsed by default in that mode, R7), rather than torn out, since
  Explorer remains useful standalone in both modes.
- No per-page choice of mode (e.g. "wizard for Compose, docked for Review") - one app-wide setting,
  same granularity as `ThemeVariant`.
- No attempt to literally restore the pre-Epic-25 Main/Diffs pages verbatim. The revived Home
  dashboard (R5) is rebuilt fresh against the current singleton-based Compose/Review; the Diffs
  page stays retired either way (`HistoryView`'s read-only `DiffsDetailControl` usage is unaffected
  by this epic).
- No live, in-session switch between modes without a restart - avoids the complexity/risk of
  tearing down and rebuilding `NavigationView`'s menu structure and whatever page is currently on
  the navigation stack.

## Requirements

1. **R1 — `InterfaceMode` setting.** New `enum InterfaceMode { EditorFocused, FlowWizard }`
   (`Paarde.Avalonia/Models/`). New `InterfaceModeSettingsKeys.Mode` key
   (`Paarde.Avalonia/Services/`), read via `ILocalSettingsService`, defaulting to `EditorFocused`
   when unset. `SettingsViewModel` gains an `InterfaceMode` `[ObservableProperty]` plus a
   persist-on-change path (same shape as `SwitchThemeCommand`) and a "Restart to apply" caption
   shown once the in-memory value differs from the persisted one.
2. **R2 — Startup page by mode.** `App.axaml.cs`'s startup navigation reads
   `InterfaceModeSettingsKeys.Mode` before calling `NavigateTo`: `EditorFocused` →
   `ExplorerViewModel` (today's behavior, unchanged); `FlowWizard` → `MainViewModel` (R5).
3. **R3 — Mode-conditional `NavigationView` items.** `ShellView.axaml.cs`'s constructor (not
   static XAML) reads the persisted mode once at startup and, only when `FlowWizard`, inserts three
   additional `NavigationViewItem`s into `NavigationViewControl.MenuItems` ahead of the
   always-present Explorer/Terminal/History items: **Home** (`MainViewModel`, R5), **Compose**
   (`PromptViewModel`, R4), **Review** (`TutorialViewModel`, R6). `EditorFocused` mode's
   `MenuItems` are unchanged from today.
4. **R4 — Compose as a page again.** Re-point the already-existing, already-registered
   `PromptView.axaml`/`.axaml.cs` at a live `NavigationView` entry when in `FlowWizard` mode. No
   changes to `PromptView.axaml`/`.axaml.cs` or `PageService` are needed - both are already correct
   against the current singleton `PromptViewModel`.
5. **R5 — Home dashboard revived.** New `MainViewModel` (`Paarde.Avalonia/ViewModels/`) + new
   `MainView.axaml`, matching the pre-Epic-25 dashboard's shape (recovered via `git show
   5072a093^:Paarde/Paarde.Avalonia/ViewModels/MainViewModel.cs`): `RecentRuns`/`RecentProjects`
   read fresh from `IRunSessionStore` in `OnNavigatedTo`, `QuickPromptText`, and
   `OpenRecentRun`/`OpenRecentProject`/`SendQuickPrompt` all navigating to the Compose page (R4)
   with a `PromptLaunchRequest`, same as the original. Registered `Configure<MainViewModel,
   MainView>()` in `PageService`, `AddTransient` (matches the original's lifetime - a dashboard
   that reloads its recent-runs/projects list on every visit, not a cross-page singleton panel).
6. **R6 — Review as a page.** New `ReviewView.axaml` (thin `UserControl`, `x:DataType
   TutorialViewModel`) resolving `TutorialViewModel` from DI in its constructor (the same singleton
   instance `AssistantPanelViewModel.Review` already holds) and hosting
   `<views:ReviewPanelControl DataContext="{Binding}" />` unchanged. No new business logic - this
   wrapper exists purely to give `TutorialViewModel` a standalone-page route again.
   `Configure<TutorialViewModel, ReviewView>()` added to `PageService`.
7. **R7 — Docked Assistant panel default-collapses in Flow/Wizard mode.** `ExplorerView.axaml.cs`
   hands `ResizablePaneHelper` a different default width (`0`, i.e. collapsed) for
   `AssistantPanelSplitter` when `InterfaceModeSettingsKeys.Mode` is `FlowWizard`, instead of
   today's non-zero default - the docked panel still fully works if the user drags it open; it is
   simply not presented by default in a mode where Compose/Review already have their own nav
   destinations. This only changes the *default* used before any width has ever been persisted for
   this mode; it does not touch `ExplorerAssistantPanelWidth`'s existing persisted value semantics.
8. **R8 — In-Wizard progression on run completion.** Today, a finished run (`IPromptResultStore`'s
   `Tutorial` property changing) only flips `AssistantPanelViewModel.SelectedTabIndex`, which has
   no effect on Flow/Wizard mode's separate pages. In `FlowWizard` mode, the same event must instead
   navigate the user to the Review page (R6) automatically - design.md decides whether this
   subscription lives on `AssistantPanelViewModel` itself (gated by reading `InterfaceMode`) or on
   a sibling that owns the `INavigationService` dependency, to avoid giving a docked-panel view
   model a page-navigation responsibility it doesn't need in `EditorFocused` mode.
9. **R9 — Settings page UI.** New toggle control on `SettingsView.axaml` (matching the
   `ThemeVariant` selector's existing visual pattern) with a one-line description of each mode and
   a "takes effect after restart" caption, per R1.

## Acceptance criteria

- **AC-1**: Given `InterfaceMode` is `EditorFocused` (the default, including on a settings store
  with no persisted value yet), launching the app lands on Explorer exactly as it does today, and
  `NavigationView` shows only Explorer/Terminal/History.
- **AC-2**: Given `InterfaceMode` is `FlowWizard`, launching the app lands on the new Home page,
  and `NavigationView` shows Home/Compose/Review/Explorer/Terminal/History, in that order.
- **AC-3**: Given `FlowWizard` mode, sending a prompt from Compose and having it finish navigates
  the user to Review automatically, showing the same narrative the docked Review tab would show in
  `EditorFocused` mode for the same run.
- **AC-4**: Given either mode, opening Explorer and dragging the Assistant panel splitter open
  shows the exact same Compose/Review content Flow/Wizard's separate pages show - same underlying
  singleton instances, not a divergent copy.
- **AC-5**: Changing the `InterfaceMode` setting and clicking "Save"/navigating away does not
  change the running session's `NavigationView` items or landing page; only the next app launch
  reflects the new value.
