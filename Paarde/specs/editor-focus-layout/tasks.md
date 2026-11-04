# Implementation tasks — Editor-first layout (zen mode, split view, tab polish)

Each task references the requirement(s) it satisfies. Not started — no code written yet.

## F28.1 — Zen/focus mode

- [ ] T1 `ExplorerViewModel.ToggleZenModeCommand` + `ExplorerView.axaml.cs` handler capturing and
  restoring both `TreeSplitter`'s and `AssistantPanelSplitter`'s widths as a pair; `Ctrl+K Z`
  keybind. _(R1)_

## F28.2 — Split editor group

- [ ] T2 `OpenFileTabViewModel.TabGroup`; `ExplorerViewModel.PrimaryGroupTabs`/
  `SecondaryGroupTabs` computed projections over `OpenTabs`. _(R2)_
- [ ] T3 `ExplorerView.axaml`: second `TabControl` + splitter inside the existing tab-area column,
  collapsed to width `0` when no tab has `TabGroup == 1`; "Split right" context-menu action on the
  active tab setting `TabGroup`; closing the secondary group's last tab collapses the column back
  automatically. New `WorkspaceLayoutSettingsKeys.ExplorerSplitGroupWidth`. _(R2)_

## F28.3 — Tab polish

- [ ] T4 `OpenFileTabViewModel.IsPinned`; pin/unpin tab context-menu action; `OpenTabs`'
  `ItemsSource` binds through a stable `OrderByDescending(IsPinned)` projection rather than
  resorting the underlying collection; pinned-tab icon. _(R3)_
- [ ] T5 Inspect the installed FluentAvalonia/Avalonia `TabControl` version for existing
  drag-reorder support; if absent, implement manual `PointerPressed`/`PointerMoved`/
  `PointerReleased` reorder calling `OpenTabs.Move`. _(R4)_
- [ ] T6 `ExplorerViewModel._mruOrder` tracking (updated on every `SelectedTab` change);
  `Ctrl`+repeated-`Tab` popup (same `Popup`/`ListBox` shape as Quick Open) cycling through it;
  releasing `Ctrl` activates the highlighted tab. _(R5)_

## F28.4 — Default widths

- [ ] T7 Reduce `ExplorerTreeWidth`'s/`ExplorerAssistantPanelWidth`'s first-run-only default
  widths in `ExplorerView.axaml.cs`'s `ResizablePaneHelper` construction so the editor's `*`
  column visibly dominates on first launch; no change to already-persisted values' semantics.
  _(R6)_

## F28.5 — Tests

- [ ] T8 `ResizablePaneHelperTests`: single-pane collapse/restore (regression guard) plus a new
  paired collapse/restore test for R1. _(validates R1, R7)_
- [ ] T9 `ExplorerViewModelTests`/`OpenFileTabViewModelTests` extensions: pin-sort stability,
  `TabGroup` projections across add/close/reassign, `_mruOrder` updates including closing the
  current front entry. _(validates R2, R3, R5, R7)_
- [ ] T10 Manual verification: zen mode collapses/restores both panes together; split-right shows
  two independently scrollable tab areas; pinning/reordering tabs behaves as expected and survives
  opening/closing other tabs; `Ctrl+Tab` cycles and activates correctly; a fresh settings store
  opens Explorer with the editor visibly dominant. _(validates AC-1 through AC-6; same
  sandboxed-environment limitation as every other Avalonia-rendering manual-verification task in
  this roadmap)_
