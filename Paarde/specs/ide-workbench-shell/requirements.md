# Requirements — Persistent workbench shell

## Status

Draft. Not started. This is Epic 22 (`ROADMAP.md`) — referenced elsewhere in this repo as
`[[ide-workbench-shell]]` (`specs/build-and-run/requirements.md`). Part of the broader
"wizard → IDE" structural initiative (Epics 20, 22–24); Epic 20 shipped the first concrete slice
(a persistent Build/Run/Stop toolbar and a single output strip in `ShellView`). This epic
generalizes that strip into a real multi-tab output **dock** and adds the other piece every IDE
has that a page-based wizard doesn't: a **status bar**.

## Context and current state

Confirmed by direct inspection, 2026-10-27:

- `ShellView.axaml`'s row 2 is a single collapsible `Expander` ("Output") hosting one
  `TerminalControl` bound to `RunPanelViewModel.OutputBuffer` — the same buffer carries both Build
  and Run output (cleared and re-fed on each action), so there's no way to look at a prior build's
  output while a run is active, or vice versa.
- Epic 16's Terminal page (`TerminalView`/`TerminalViewModel`) is a separate, full `NavigationView`
  page with its own independent `TerminalBuffer` and keyed `IPtyProcessManager` — functionally a
  second, disconnected output surface rather than another tab of the same dock.
- Epic 17/18's LSP diagnostics (`ILanguageServerSessionManager.DiagnosticsPublished`) are only ever
  consumed by `ExplorerViewModel.Diagnostics`, filtered down to whichever single file is currently
  open — there is no aggregated "every problem across every open file" view anywhere, the thing
  most IDEs call a Problems panel.
- There is no status bar anywhere. `IGitStatusService.GetStatus` already computes
  `IsRepository`/`DirtyFiles`, but nothing surfaces it persistently; there's no branch name on
  `GitStatus` at all. `ILanguageServerSessionManager` tracks which per-language servers are running
  internally (`_clientsByServerGroup`) but doesn't expose it. `CodeEditor` has no bindable caret
  position.

## Goals

1. Generalize Epic 20's single Build/Run output strip into a tabbed dock — Build, Run, Problems,
   Terminal — in the same `ShellView` location, visible from every page.
2. Build and Run get their own independent output buffers (today they share one), so switching
   configurations or starting a new run doesn't erase the other's last output.
3. "Terminal" absorbs Epic 16's `TerminalView` page's PTY-backed command-running functionality
   into the dock, rather than remaining a second, disconnected full-page surface.
4. "Problems" surfaces LSP diagnostics aggregated across every file that's been opened this
   session, not just whichever one Explorer currently shows.
5. The dock remembers which tab was last active and whether it was expanded or collapsed, across
   navigation and (same persisted-UI-state convention as working directory/theme/window size)
   across app restarts.
6. A thin status bar, anchored at the very bottom of `ShellView` below the dock, shows: current git
   branch + dirty-file count, the active `RunConfiguration`'s name, a per-language code-intelligence
   connection indicator, and — only while a `CodeEditor` has focus — the caret's line:column.
7. Nothing here requires touching any individual page's own content; this is additive to
   `ShellView` only, the same scope discipline Epic 20 used.

## Non-goals

- A general-purpose docking framework (floating/undockable/rearrangeable panels, drag-to-detach).
  Four fixed tabs in one fixed location is the full extent of "dock" here — matching exactly what
  the roadmap asks for, not a VS Code-style arbitrary layout.
- Removing Epic 16's `TerminalView` page/`NavigationViewItem` from the app. It stays as a
  deep-link/muscle-memory entry point, now rendering the same dock-owned `TerminalViewModel`
  instance instead of its own — see design.md's "Terminal placement" for why a second, independent
  instance isn't an option.
- A real debugger-style Problems experience (click-to-jump, filtering, grouping by severity).
  Problems here is a flat, read-only list: file path, severity, message, line:column — matching
  Epic 17/18's existing `LspDiagnostic` shape, enough to see "what's currently broken" at a glance.
- Any change to how `IDiagnosticsAggregator`'s underlying per-file diagnostics are *computed* —
  that's still entirely `ILanguageServerSessionManager.DiagnosticsPublished` (Epic 17/18); this
  epic only adds a place that remembers every file's latest result instead of just one.
- Editing files directly from the status bar's dirty-file count or the Problems list — both are
  read-only surfaces; navigating to the actual file is Explorer's job (Epic 23's tabs make that
  more useful but aren't required here).

## Requirements

**R1 — Multi-tab output dock**
- R1.1 `ShellView`'s single output `Expander`/`TerminalControl` is replaced by a tabbed dock with
  four tabs: Build, Run, Problems, Terminal.
- R1.2 Build and Run each have their own `TerminalBuffer`, so viewing one's output never requires
  discarding or overwriting the other's.
- R1.3 The Terminal tab hosts the same PTY-backed command-running UI Epic 16's `TerminalView` page
  already has (command input box, history, live PTY output) — not a reimplementation — bound to a
  single shared `TerminalViewModel` instance.
- R1.4 The Problems tab lists every diagnostic currently known across every file opened this
  session (R4), with enough information to identify which file and where (file path, severity,
  message, line:column).
- R1.5 The dock occupies the same `ShellView` location Epic 20's strip did and is visible
  regardless of which page is navigated to (same reachability guarantee Epic 20's R5.1 already
  established).

**R2 — Dock persistence**
- R2.1 The dock remembers which tab was last active across page navigation within a session.
- R2.2 The dock remembers whether it was expanded or collapsed, and which tab was active, across
  app restarts — same persisted-UI-state convention (`ILocalSettingsService`) already used for
  working directory/theme/window size/pane widths.

**R3 — Status bar**
- R3.1 A thin bar anchored at the very bottom of `ShellView`, below the dock, below everything
  else, visible regardless of which page is navigated to.
- R3.2 Shows the current working directory's git branch name and dirty-file count (extends
  `IGitStatusService`/`GitStatus`, which already computes `IsDirty`/`DirtyFiles` but not branch).
- R3.3 Shows the currently selected `RunConfiguration`'s name (or an explicit "no configuration"
  state when none is selected/detected).
- R3.4 Shows, per configured language, whether its code-intelligence server is currently running
  (extends `ILanguageServerSessionManager`, which tracks this internally but doesn't expose it).
- R3.5 When a `CodeEditor` currently has keyboard focus (Explorer's viewer, Tutorial's proposal
  editor), shows that editor's caret position as 1-based line:column. Shows nothing in that slot
  when no `CodeEditor` has focus.

**R4 — Diagnostics aggregation**
- R4.1 A new `IDiagnosticsAggregator` holds the most recent diagnostics for every file path it's
  been told about, independent of which file Explorer currently has open.
- R4.2 It's fed the same `ILanguageServerSessionManager.DiagnosticsPublished` event
  `ExplorerViewModel` already subscribes to — not a second, parallel LSP subscription.
- R4.3 A file's entry is removed once that file reports zero diagnostics (fixed, not just stale) —
  the Problems tab should never show a diagnostic for a file that's no longer broken.
- R4.4 Reading the current aggregated set never requires an open file/page — it reflects every file
  touched this session, independent of Explorer's own lifecycle.

## Open questions (resolved in design.md)

- Whether the dock's four tabs each get their own expand/collapse state or share one — see
  design.md.
- Whether removing a file's diagnostics on R4.3 should also happen when a file is closed (not just
  fixed) — see design.md.
