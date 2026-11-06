# Requirements — Editor-first layout (zen mode, split view, tab polish)

## Status

Draft. Not started. This is Epic 28 (`ROADMAP.md`) — referenced elsewhere in this repo as
`[[editor-focus-layout]]`. Builds on Epic 25's existing collapsible-pane convention
(`ResizablePaneHelper`) and on `[[interface-mode]]` (Epic 26): these layout affordances are
specific to `EditorFocused` mode's `ExplorerView` - `FlowWizard` mode's separate Compose/Review
pages have no tree/Assistant-panel layout to zen/split in the first place. Independent of
`[[editor-intelligence]]` (Epic 27); either can land first.

## Context and current state

Confirmed by direct inspection, 2026-11-03:

- `ExplorerView.axaml`'s `WorkspaceGrid`: fixed `300`px tree | splitter | `*` tab area | splitter |
  `360`px Assistant panel, all persisted via `WorkspaceLayoutSettingsKeys`/`ResizablePaneHelper`,
  already individually collapsible by dragging a splitter to width `0` or (for the Assistant panel
  only) clicking `ToggleAssistantPanelButton`.
- A single `TabControl` with one `ContentTemplate` slot (`ExplorerFileTabControl`) - only one tab's
  content is ever visible at a time. No split-pane/multi-group concept exists anywhere in Explorer
  today.
- `OpenFileTabViewModel`/`ExplorerViewModel.OpenTabs` (an `ObservableCollection`) has no
  pinned/ordering-beyond-insertion concept. `TabControl`'s own drag-reorder support (if any) is
  not currently wired to persist anything. There is no keyboard MRU (most-recently-used) switcher
  - Quick Open (Epic 24) covers "open a new or already-open file by name," not "cycle through
  recently-viewed tabs."

## Goals

- Make full-width, uninterrupted editing the one-keystroke default, and side-by-side comparison a
  first-class action - rather than both tree and Assistant panel permanently occupying
  fixed-proportion space regardless of whether the user needs them open right now.
- **Zen/focus mode**: one shortcut/toggle collapses tree + Assistant panel together, restoring
  both to their prior widths on toggle-off.
- **Split editor view**: show two tabs side by side within the tab area, each independently
  scrollable/selectable.
- **Tab quality-of-life**: pin a tab, drag-to-reorder (persisted), `Ctrl+Tab` MRU cycling.
- **Default pane widths tuned** so a first-run user's editor visibly dominates the screen rather
  than the tree/editor/Assistant-panel three-way split looking roughly equal.

## Non-goals

- No custom minimap (same rationale `[[editor-intelligence]]` already gives: no built-in
  AvaloniaEdit support, large bespoke effort for comparatively low value here).
- No more than two split panes ("split view," not a general tiling-window-manager grid) - matches
  every mainstream editor's default split affordance without building a full tiling layout engine.
- No change to `FlowWizard` mode's Compose/Review pages - this epic's layout affordances are
  scoped to `ExplorerView` only.
- No per-tab independent zoom/font size - unrelated to this epic's focus-mode/split-view goals.
- No persistence of pinned-tab state or split-group membership across app restarts - matches
  today's existing behavior that `OpenTabs` itself does not persist across restarts either.

## Requirements

1. **R1 — Zen/focus mode toggle.** New command + keybind (`Ctrl+K Z` - avoiding collision with
   the existing `Ctrl+P`/`Ctrl+Shift+P`/[[editor-intelligence]]'s `Ctrl+Shift+O`) on
   `ExplorerViewModel`, collapsing `TreeSplitter`'s and `AssistantPanelSplitter`'s panes to width
   `0` together (reusing `ResizablePaneHelper`'s existing collapse convention) and remembering
   both panes' pre-collapse widths to restore on toggle-off - the same width-remembering behavior
   `ToggleAssistantPanelButton` already provides for the Assistant panel alone, now covering both
   panes atomically.
2. **R2 — Split editor group.** A second tab-content "slot" within `ExplorerView`'s existing tab
   area, toggled via a "Split right" action on the active tab's context menu/toolbar, sized via a
   new splitter and its own `WorkspaceLayoutSettingsKeys` entry *inside* the existing tab-area
   column (not a new top-level `WorkspaceGrid` column). design.md decides the exact data-model
   split (a second `OpenTabs`-like collection vs. a per-tab "group" field on the existing one).
3. **R3 — Pinned tabs.** `IsPinned` (`bool`, `[ObservableProperty]`) on `OpenFileTabViewModel`,
   toggled via the tab's context menu. Pinned tabs sort before unpinned ones in `OpenTabs` and are
   visually distinguished (icon). Session-only - not persisted across restarts (matches `OpenTabs`
   itself).
4. **R4 — Drag-to-reorder.** Wire tab drag gestures to reorder `OpenTabs` in place. Confirm by
   direct inspection of the installed FluentAvalonia/Avalonia `TabControl` version whether
   drag-reorder is already supported out of the box before committing to a manual
   `PointerPressed`/`PointerMoved` implementation.
5. **R5 — `Ctrl+Tab` MRU switcher.** `ExplorerViewModel` tracks a most-recently-activated tab
   order (separate from `OpenTabs`' display order). Holding `Ctrl` and repeatedly pressing `Tab`
   opens a transient popup (same `Popup`/`ListBox` shape Quick Open/[[editor-intelligence]]'s
   symbol quick-pick already use) cycling through that order; releasing `Ctrl` activates the
   highlighted tab - the same interaction convention every mainstream editor/IDE's own `Ctrl+Tab`
   already uses.
6. **R6 — Default pane width tuning.** Reduce `ExplorerTreeWidth`'s and
   `ExplorerAssistantPanelWidth`'s *first-run* defaults only (the fallback `ResizablePaneHelper`
   uses before any value has ever been persisted for that key) so the editor's `*` column visibly
   dominates on first launch. Does not change any already-persisted user preference.
7. **R7 — Tests.** `ResizablePaneHelper` collapse/restore pair for R1 (both panes together, not
   just one); `OpenTabs` pin-sorts-first ordering for R3; MRU-order tracking for R5 (pure logic, no
   live `TextArea` or rendered `TabControl` needed for any of these three).

## Acceptance criteria

- **AC-1**: Triggering zen mode with both tree and Assistant panel open collapses both to width 0
  in one action; triggering it again restores both to their exact pre-collapse widths.
- **AC-2**: "Split right" on an open tab shows a second tab-content area beside the first, each
  independently selectable/scrollable, sized via its own splitter.
- **AC-3**: Pinning a tab moves it before all unpinned tabs in the tab strip and keeps it there as
  more tabs are opened/closed.
- **AC-4**: Dragging a tab to a new position in the strip persists that order for the remainder of
  the session (not required to survive a restart, per the non-goals).
- **AC-5**: Holding `Ctrl` and pressing `Tab` repeatedly cycles a popup through recently-activated
  tabs in most-recent-first order; releasing `Ctrl` activates whichever tab is highlighted.
- **AC-6**: A brand-new settings store (no persisted pane widths) opens Explorer with the editor
  occupying visibly more than half the window's width.
