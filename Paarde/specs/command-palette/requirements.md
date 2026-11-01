# Requirements — Command palette & Quick Open

## Status

Draft. Not started. This is Epic 24 (`ROADMAP.md`) — referenced elsewhere in this repo as
`[[command-palette]]`. Last of the four committed "wizard → IDE" epics (20, 22, 23, 24); explicitly
sequenced after the other three so the palette has something to expose (Build/Run/Stop, dock tabs,
Explorer file tabs) instead of being populated with less.

## Context and current state

Confirmed by direct inspection, 2026-10-30:

- There is no global keyboard-shortcut infrastructure anywhere in the app. The only existing
  `KeyGesture` is `App.axaml.cs`'s `window.AttachDevTools(new KeyGesture(Key.F12, ...))` — a
  one-off, not a registry.
- Epic 14 already built the closer-than-you'd-think precedent for both popups: `PromptView.axaml`'s
  `@mention` picker is a `Popup` (`PlacementTarget`, `IsLightDismissEnabled="True"`, bound
  `IsOpen`) containing a `ListBox`, driven by `MentionTriggerDetector` (parses the trigger out of
  typed text) and `FileMentionMatcher.FindMatches` (substring match over a cached `FileTreeEntry`
  tree from `FileTreeBuilder.Build`, already gitignore-aware). Quick Open needs the same filtered
  list and the same popup shape, just reached by a global shortcut instead of an in-textbox `@`.
- `INavigationService.NavigateTo(string pageKey, object? parameter = null, bool clearNavigation =
  false)` already accepts an arbitrary `parameter`; `ExplorerViewModel.OnNavigatedTo(object
  parameter)` already receives it but currently ignores it entirely, always reloading the saved
  working directory and calling `RefreshTree()`.
- `ExplorerViewModel` is registered `AddTransient`, not `AddSingleton` — but `OpenTabs`
  (`ObservableCollection<OpenFileTabViewModel>`, Epic 23) already survives leaving and returning to
  the Explorer page in practice, because `ExplorerView`'s own constructor calls
  `App.GetService<ExplorerViewModel>()` exactly once and the `NavigationService`'s `Frame` caches
  page instances — the same "effectively one instance for as long as the page lives" shape Epic 22
  relied on when it promoted `TerminalViewModel` to a real `AddSingleton` for a different reason
  (sharing one instance between two simultaneously-visible surfaces, which Quick Open does not need
  here).
- `ShellView.axaml`'s `NavigationView.MenuItems` is the complete, already-explicit page registry:
  seven `NavigationViewItem`s (`Content` + `helpers:NavigationHelper.NavigateTo="<ViewModel full
  type name>"` pairs — Main, Prompt, Explorer, Terminal, Diffs, Tutorial, History) plus Settings
  via `NavigationViewService.SettingsItem`.
- `RunPanelViewModel` (DI singleton, already owned by `ShellViewModel`) exposes `BuildCommand`,
  `RunCommand`, `StopCommand` as `IAsyncRelayCommand`/`IRelayCommand` with their own
  `CanExecute` gating (`CanBuild`/`CanRun`/`CanStop`) already wired to
  `NotifyCanExecuteChanged()` on the relevant property changes.
- `IWorkingDirectoryContext` is a fire-and-forget pub/sub singleton (`event Action<string>?
  Changed; void SetCurrent(string path);`) with no stored "current value" property — every
  existing consumer (`RunPanelViewModel`, `StatusBarViewModel`, `WorkbenchDockViewModel`) instead
  reads its own copy from `ILocalSettingsService`
  (`WorkingDirectorySettingsKeys.WorkingDirectory`) once at construction and keeps it fresh via the
  `Changed` event — not a new pattern to introduce, just the one this epic's new view model must
  also follow.

## Goals

1. `Ctrl+P` anywhere in the app opens a **Quick Open** popup: fuzzy-ish (substring, same as Epic
   14) filename search over the current working directory's gitignore-aware file tree.
2. Selecting a Quick Open result opens or activates that file as an Explorer tab (Epic 23's
   `OpenOrActivateTab`), without discarding any other open tab, and navigates to the Explorer page
   if it isn't already showing.
3. `Ctrl+Shift+P` anywhere in the app opens a **Command palette** popup: a small, explicit,
   hand-written registry of named commands — Build, Run, Stop, and one "Go to `<Page>`" entry per
   existing navigable page — filterable by substring, not a reflection-based scan of every
   `[RelayCommand]` in the app.
4. Both popups are reachable identically regardless of which page is currently navigated to (same
   reachability guarantee Epic 20's R5.1 and Epic 22's R1.5/R3.1 already established for the
   toolbar/dock/status bar).
5. Nothing here requires touching any individual page's own content beyond the one, already-general
   `ExplorerViewModel.OnNavigatedTo` parameter hook — this is additive to `ShellView` plus that one
   existing extension point, the same scope discipline Epics 20/22 used.

## Non-goals

- A general "search everything" surface (symbols, commit history, in-file text search). Quick Open
  is filenames only, matching the explicit `ROADMAP.md` ask.
- Reflection/introspection over every `[RelayCommand]` in the app for the command palette registry.
  An explicit, hand-maintained list keeps this from surfacing half-finished or
  page-context-dependent commands that would do nothing (or throw) if invoked from the wrong place
  — the same reasoning `ROADMAP.md`'s Epic 24 text already gives.
- True fuzzy/scored matching (e.g. subsequence scoring, typo tolerance) or most-recently-used
  ranking. Both popups reuse Epic 14's existing substring-match-then-alphabetical-sort behavior;
  anything smarter is a fast-follow, not part of this epic's first pass.
- User-customizable or remappable keybindings. `Ctrl+P`/`Ctrl+Shift+P` are the only two gestures
  this epic adds, hardcoded, matching the one existing `KeyGesture` precedent
  (`App.axaml.cs`'s F12).
- Promoting `ExplorerViewModel` to a DI singleton. The existing `NavigateTo(pageKey, parameter)`
  hook is sufficient for Quick Open to reach the already-running Explorer page instance; see
  design.md.
- Any change to `FileMentionMatcher`'s matching algorithm. Quick Open calls the existing method
  directly rather than forking or generalizing it.

## Requirements

**R1 — Quick Open (`Ctrl+P`)**
- R1.1 `Ctrl+P`, pressed anywhere in the app, opens a popup independent of the currently navigated
  page.
- R1.2 Typing filters the working directory's files by substring match on relative path (reusing
  `FileMentionMatcher.FindMatches` over a `FileTreeBuilder.Build` tree, gitignore-aware, same as
  Epic 14), case-insensitive, alphabetically ordered.
- R1.3 `Up`/`Down` move the highlighted result; `Enter` or a click/double-tap on a result activates
  it; `Escape` closes the popup without navigating anywhere.
- R1.4 Activating a result navigates to the Explorer page (if not already there) and opens or
  activates a tab for that file, reusing Epic 23's existing open-or-activate behavior — it must
  never discard any other tab's unsaved edits.
- R1.5 If no working directory is configured (or the configured path no longer exists), Quick Open
  shows an explicit empty state rather than throwing or silently doing nothing.

**R2 — Command palette (`Ctrl+Shift+P`)**
- R2.1 `Ctrl+Shift+P`, pressed anywhere in the app, opens a popup listing a small, explicit,
  hardcoded registry of named commands.
- R2.2 The initial registry is: "Build", "Run", "Stop" (delegating to `RunPanelViewModel`'s
  existing `BuildCommand`/`RunCommand`/`StopCommand`, including their existing enabled/disabled
  state), plus one "Go to `<Page>`" entry per page already reachable from
  `ShellView.axaml`'s `NavigationView` (Main, Prompt, Explorer, Terminal, Diffs, Tutorial, History,
  Settings).
- R2.3 Typing filters the registry by substring match on command name, case-insensitive.
- R2.4 A command whose `CanExecute` currently returns `false` (e.g. "Stop" when nothing is running)
  is shown disabled, not hidden — consistent with every other command surface in this app showing
  `CanExecute` state rather than hiding unavailable actions.
- R2.5 `Up`/`Down` move the highlighted command; `Enter` or a click/double-tap executes the
  highlighted (enabled) command and closes the popup; `Escape` closes without executing anything.

**R3 — Shared popup behavior & keybindings**
- R3.1 `Ctrl+P` and `Ctrl+Shift+P` are recognized regardless of which control currently has
  keyboard focus — new global key bindings, since none exist today beyond the single
  `App.axaml.cs` DevTools gesture.
- R3.2 At most one of the two popups is open at a time; invoking the other's shortcut while one is
  open closes the first and opens the second (never both stacked).
- R3.3 Both popups reuse Epic 14's existing popup visual shape (a `Popup` containing a `ListBox`,
  light-dismiss enabled) rather than introducing a new visual pattern for the same kind of UI.

## Open questions (resolved in design.md)

- Whether Quick Open needs its own `FileTreeEntry` cache or can share `PromptViewModel`'s — see
  design.md (kept separate, same duplication Epic 14 already has between its own call sites,
  not a shared service introduced for two consumers).
- Where the global `KeyBinding`s are attached (the `Window` vs. `ShellView`) — see design.md.
- How Quick Open's "open this file in Explorer" step reaches the already-running
  `ExplorerViewModel` instance without promoting it to a singleton — see design.md.
