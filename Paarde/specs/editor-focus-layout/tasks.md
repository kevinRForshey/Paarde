# Implementation tasks — Editor-first layout (zen mode, split view, tab polish)

Each task references the requirement(s) it satisfies.

## F28.1 — Zen/focus mode

- [x] T1 `ExplorerViewModel.ToggleZenModeCommand` + `ExplorerView.axaml.cs` handler capturing and
  restoring both `TreeSplitter`'s and `AssistantPanelSplitter`'s widths as a pair; `Ctrl+K Z`
  keybind. _(R1)_

## F28.2 — Split editor group

- [x] T2 `OpenFileTabViewModel.TabGroup`; `ExplorerViewModel.PrimaryGroupTabs`/
  `SecondaryGroupTabs` - real, independently ordered `ObservableCollection`s (not LINQ
  projections) so FluentAvalonia `TabView`'s native drag-reorder has a stable list to mutate
  directly. _(R2)_
- [x] T3 `ExplorerView.axaml`: second `ui:TabView` + splitter inside the existing tab-area column,
  collapsed to width `0` when `SecondaryGroupTabs` is empty; "Split right" context-menu action on
  the active tab setting `TabGroup`; closing the secondary group's last tab collapses the column
  back automatically. New `WorkspaceLayoutSettingsKeys.ExplorerSplitGroupWidth`. _(R2)_

## F28.3 — Tab polish

- [x] T4 `OpenFileTabViewModel.IsPinned`; pin/unpin tab context-menu action; pinning repositions
  the tab to right after the last already-pinned tab in its group (one-time move, not a
  continuously-enforced sort, so it doesn't fight drag-reorder afterward); pinned-tab icon. _(R3)_
- [x] T5 Confirmed FluentAvalonia 2.5.1's `ui:TabView` ships native `CanDragTabs`/`CanReorderTabs`
  drag-reorder (via decompilation - no manual `PointerPressed`/`PointerMoved`/`PointerReleased`
  handling needed); both primary/secondary `TabView`s bind `TabItems` to real
  `ObservableCollection`s so the control can mutate them directly. _(R4)_
- [x] T6 `ExplorerViewModel._mruOrder` tracking (updated on every `SelectedTab` change, pruned on
  tab close); `OpenMruSwitcher`/`AdvanceMruSwitcher`/`CommitMruSwitcher`; `ExplorerView.axaml.cs`'s
  `Ctrl`+repeated-`Tab` `KeyDown`/`KeyUp` handling plus a `Popup`/`ListBox` (same shape as Quick
  Open) cycling through it; releasing `Ctrl` activates the highlighted tab. _(R5)_

## F28.4 — Default widths

- [x] T7 Reduced `ExplorerTreeWidth` (300→220) and `ExplorerAssistantPanelWidth` (360→280)
  first-run-only default widths in `ExplorerView.axaml.cs`'s `ResizablePaneHelper` construction so
  the editor's `*` column visibly dominates on first launch; no change to already-persisted
  values' semantics. _(R6)_

## F28.5 — Tests

- [x] T8 `ResizablePaneHelperTests`: single-pane collapse/restore (regression guard) plus a new
  paired collapse/restore test (two independent helpers, each tracking its own pre-collapse
  width) for R1. _(validates R1, R7)_
- [x] T9 `ExplorerViewModelTests`/`OpenFileTabViewModelTests` extensions: pin-sort stability
  (pin-to-front-of-group, unpin leaves position), `TabGroup`/group-collection membership across
  add/close/reassign, `_mruOrder` updates including closing the current front entry. _(validates
  R2, R3, R5, R7)_
- [x] T10 Manual verification: zen mode collapses/restores both panes together; split-right shows
  two independently scrollable tab areas; pinning/reordering tabs behaves as expected and survives
  opening/closing other tabs; `Ctrl+Tab` cycles and activates correctly; a fresh settings store
  opens Explorer with the editor visibly dominant. _(validates AC-1 through AC-6; same
  sandboxed-environment limitation as every other Avalonia-rendering manual-verification task in
  this roadmap - no X server/real LSP servers available here)_
  Done on Windows in Paarde.Avalonia. Fixed along the way: the tab right-click menu (Pin / Split
  right) never opened - a `ContextMenu` on a FluentAvalonia `TabViewItem` doesn't stay open, so it's
  a `ContextFlyout` now; pinning blanked the editor and split view showed two blank groups, because
  both TabViews bound `SelectedItem` two-way to the one `SelectedTab` - ExplorerView now syncs each
  group's selection itself, like Paarde.WinUI; `Ctrl+Tab` did nothing with focus in the file tree,
  since the tree handles Tab first - the key handler is a tunnel handler now. Not covered: dragging
  a tab to reorder it by mouse.
