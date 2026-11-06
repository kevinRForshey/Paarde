# Design — Editor-first layout (zen mode, split view, tab polish)

## Summary of architectural change

All five features (zen mode, split view, pin, drag-reorder, MRU switcher) are additive changes to
`ExplorerViewModel`/`ExplorerView.axaml` and `OpenFileTabViewModel` - no change to
`CodeEditor`, `ProposedFileChangeViewModel`, or anything outside Explorer's own tab/pane
management. The one genuinely new concept is the split editor group (R2); everything else extends
data already on `OpenFileTabViewModel`/`ExplorerViewModel` or reuses `ResizablePaneHelper`'s
existing collapse convention.

## New/changed types

### 1. Zen/focus mode (R1)

`ExplorerViewModel` gains a `ToggleZenModeCommand`. `ExplorerView.axaml.cs` already owns both
`ResizablePaneHelper` instances (tree, Assistant panel) as private fields; the command's handler
(exposed via a small event or direct method call from the view's code-behind, same "ViewModel
raises, View's code-behind that owns the live `ColumnDefinition`s reacts" pattern
`OnToggleAssistantPanelButtonClick` already uses) captures both panes' current widths before
setting them to `0`, and restores the captured pair on toggle-off - a 2-tuple instead of
`ToggleAssistantPanelButton`'s single remembered width, otherwise the identical mechanism.

### 2. Split editor group (R2)

`OpenFileTabViewModel` gains a `TabGroup` property (`int`, `0` or `1` - exactly two groups, per
the "split view, not tiling" non-goal). `ExplorerViewModel.OpenTabs` stays a single flat
`ObservableCollection<OpenFileTabViewModel>`; `ExplorerView.axaml` filters it into two
`CollectionView`-backed `TabControl`s via two computed properties
(`PrimaryGroupTabs`/`SecondaryGroupTabs`, simple LINQ `.Where(t => t.TabGroup == 0/1)` projections
recomputed on `OpenTabs.CollectionChanged`) rather than maintaining two separate
`ObservableCollection`s that could drift out of sync with each other or with
`_promptResultStore.Files`-driven auto-open logic (Epic 25's R4). "Split right" sets the active
tab's `TabGroup` to `1` (creating the second `TabControl` on demand, collapsed/hidden when no tab
has `TabGroup == 1`); the second group's `TabControl` sits in a new nested column inside the
existing tab-area `Grid` cell, with its own splitter and `WorkspaceLayoutSettingsKeys
.ExplorerSplitGroupWidth` entry.

Closing the last tab in the secondary group collapses that column back to width `0` automatically
(no dangling empty pane).

### 3. Pinned tabs (R3)

`OpenFileTabViewModel.IsPinned` (`[ObservableProperty] bool`). `ExplorerViewModel.OpenTabs`'s
insertion logic (`OpenOrActivateTab`) is unchanged for *where* a new tab is inserted, but the
`TabControl`'s `ItemsSource` binds through a sorted projection (`OrderByDescending(t =>
t.IsPinned)`, stable otherwise - `OrderBy`/`OrderByDescending` are stable sorts in .NET, so
insertion order within each pinned/unpinned group is preserved) rather than resorting
`OpenTabs` itself, so `OpenTabs`' own index-based operations (close-tab-by-index, etc., if any)
keep working against insertion order rather than display order.

### 4. Drag-to-reorder (R4)

Deferred to implementation time whether FluentAvalonia's `TabControl`/`TabStripItem` already
supports drag-reorder (design.md flags this as an open question below, not resolved here, since
it requires inspecting the installed package version directly rather than reasoning about it
abstractly). If not supported out of the box, a manual `PointerPressed`/`PointerMoved`/
`PointerReleased` sequence on each tab item computes a drop index and calls
`OpenTabs.Move(oldIndex, newIndex)` - `ObservableCollection<T>.Move` already raises the correct
`CollectionChanged` notification for a live-bound `ItemsControl`.

### 5. `Ctrl+Tab` MRU switcher (R5)

`ExplorerViewModel` gains a `List<OpenFileTabViewModel> _mruOrder` (not an `ObservableCollection`
- it's an internal ordering, never bound directly), updated by moving a tab to the front whenever
it becomes `SelectedTab`. A new `MruSwitcherViewModel` (or a few more properties directly on
`ExplorerViewModel` - design.md leans toward the latter, since unlike Quick Open/Command
Palette/the symbol quick-pick this popup has no text-filtering `Query` of its own, just an
index that advances on each `Tab` keypress while `Ctrl` is held) drives a `Popup` on
`ExplorerView.axaml` listing `_mruOrder`, highlighting the current index. `KeyDown`/`KeyUp`
handling for `Ctrl`+repeated-`Tab` lives in `ExplorerView.axaml.cs` (same "keyboard interaction
lives in code-behind, state lives in the ViewModel" split every other popup in this app already
uses), releasing `Ctrl` calls `SelectedTab = _mruOrder[highlightedIndex]` and closes the popup.

### 6. Default pane widths (R6)

`ResizablePaneHelper`'s call sites for `ExplorerTreeWidth`/`ExplorerAssistantPanelWidth` in
`ExplorerView.axaml.cs` change their `defaultWidth` argument (e.g. `300` → `220`, `360` → `280` -
exact numbers decided during implementation by what leaves the editor's `*` column comfortably
dominant at a typical window size, not specified precisely here). No change to
`ResizablePaneHelper` itself or to any already-persisted value's semantics.

## Testing approach

- `ResizablePaneHelperTests` (extend existing, if present, else new): collapsing a pane to `0` and
  restoring remembers the prior width correctly; a *paired* collapse/restore (R1's zen mode, two
  panes at once) restores each independently to its own prior value, not swapped or shared.
- `OpenFileTabViewModelTests`/`ExplorerViewModelTests` extensions: pin-sorts-first ordering is
  stable within each pinned/unpinned group; `TabGroup` assignment and the
  `PrimaryGroupTabs`/`SecondaryGroupTabs` projections stay correct as tabs are added/closed/
  reassigned; `_mruOrder` updates correctly as `SelectedTab` changes, including when the
  currently-first entry is closed.
- No automated test can exercise live drag-reorder gestures, the rendered split-view layout, or
  the `Ctrl+Tab` popup's actual on-screen appearance/timing - same sandboxed-environment limitation
  this repo has documented for every other Avalonia-rendering feature. Covered by code inspection
  plus a manual-verification task in `tasks.md`.

## Open questions

- **Does FluentAvalonia's currently-installed `TabControl` support drag-reorder out of the box?**
  Not resolved here - requires inspecting the installed package version directly during
  implementation (R4/T-level task), not something this design document can determine without
  running code.
- **Exact default-width numbers for R6.** Left to implementation/visual judgment at a normal
  window size, not specified precisely in requirements.md or here.
