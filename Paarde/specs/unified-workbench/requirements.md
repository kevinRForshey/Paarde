# Requirements — Unified workbench (Assistant panel docked beside the editor)

## Status

Draft. Not started. This is Epic 25 (`ROADMAP.md`) — referenced elsewhere in this repo as
`[[unified-workbench]]`. Previously flagged "P3 — explicitly optional, not committed"; this spec
commits to it, combining what `ROADMAP.md` had sketched as two separate options: (2) dock an AI
chat/prompt panel beside the editor, and (3) fully unify the Prompt/Diffs/Tutorial pages into one
dockable workbench surface centered on the editor. Sequenced after Epics 20–24 (Build & Run,
terminal emulation, workbench shell, tabbed Explorer editing, command palette/Quick Open), all of
which this epic builds directly on top of.

## Context and current state

Confirmed by direct inspection, 2026-10-31:

- Today the app's core loop is three separate full pages reached via `ShellView.axaml`'s
  `NavigationView`: **Main** (landing dashboard: quick-prompt box, recent runs/projects, a 5-step
  getting-started walkthrough), **Prompt** (compose + run), and **Tutorial** (review narrative +
  apply, with its own per-file tab strip and embedded `CodeEditor`). A fourth page, **Diffs**, is a
  thinner, mostly-redundant view of the same `ProposedFileChangeViewModel` data Tutorial already
  shows, whose only distinguishing feature is jump-to/from-Tutorial navigation
  (`DiffJumpRequest`/`TutorialJumpRequest`). **Explorer** is a fifth, separate page for browsing
  and editing arbitrary files in the working directory (Epic 23's tabbed editing), currently
  unaware of in-flight prompt results except for a read-only "touched" dot on tree nodes.
- `IPromptResultStore` (DI singleton, `Paarde.Core/Services/PromptResultStore.cs`) already holds
  `Tutorial`/`WorkingDirectoryPath`/`SessionId`/`Files` (a fixed, mutated-in-place
  `ObservableCollection<ProposedFileChangeViewModel>`) and is explicitly documented as supporting
  live `PropertyChanged`/`CollectionChanged` subscription, not just page-navigation-time reads —
  both the Diffs and Tutorial pages already read the exact same singleton instance today, they just
  each only look at it when navigated to.
- `ProposedFileChangeViewModel` (`Paarde.Core/ViewModels/`) is the one shared diff/apply model:
  `OriginalContent`/`ProposedContent`/`EditedContent`, `Hunks`, reactively-recomputed
  `DiffLines`/`DiffHunks`, hunk-selection rollups, `BuildSelectedContent()`. Nothing about it is
  page-specific.
- `Paarde.Avalonia/Controls/CodeEditor.cs` already exposes `DiffLines`/`DiffHunks` as generic,
  reusable `StyledProperty`s with a `DiffHunkActionRequested` event — paired with
  `DiffBackgroundRenderer` (green/red bands) and `DiffHunkGutterMargin` (per-hunk Accept/Reject
  gutter buttons), both already shipped in Epic 13. Both tolerate null (paint/measure nothing) and
  are today bound only by `TutorialDetailControl.axaml`'s editor instance.
  `ExplorerFileTabControl.axaml`'s editor instance binds `GhostTextSuggestion`/`CompletionItems`/
  `Diagnostics` but not `DiffLines`/`DiffHunks` — wiring diffs into Explorer's own tabs is additive,
  not a rework of `CodeEditor` itself.
- `OpenFileTabViewModel` (`Paarde.Avalonia/ViewModels/`, Epic 23) is Explorer's plain per-tab model
  (`Content`/`IsDirty`/`FileOverview`/`Diagnostics`/`GhostTextSuggestion`/`CompletionItems`) and has
  no notion of a pending LLM-proposed change for that file — structurally unrelated to
  `ProposedFileChangeViewModel` today.
- `ExplorerViewModel.OpenOrActivateTab(filePath)` (private) is already the one reusable "open or
  focus a tab" entry point — used by tree-node clicks, Quick Open (Epic 24), and LSP
  go-to-definition/find-references (Epic 17). `RefreshTree()` already computes `touchedFullPaths`
  from `_promptResultStore.Files` to flag tree nodes `IsTouched` — the one existing live link
  between a prompt run's output and Explorer today.
- `ExplorerViewModel` is registered `AddTransient`, but in practice behaves like a singleton for as
  long as the app runs: `ExplorerView`'s constructor calls `App.GetService<ExplorerViewModel>()`
  exactly once, and `NavigationService`'s underlying `Frame` caches page (View) instances, so
  `OpenTabs` already survives leaving and returning to the Explorer page — the same mechanism
  `specs/command-palette/requirements.md` already documented and relied on. This epic's docked
  Assistant panel needs a *stronger* guarantee than that, though: it must be reachable from pages
  other than Explorer (History's Fork/Rerun) without depending on Explorer's View ever having been
  constructed yet, and without risking a second, orphaned instance. `TerminalViewModel` (Epic 22)
  already set the precedent for promoting a view model to a real `AddSingleton` for exactly this
  kind of "reachable from more than one place that isn't a navigation parent/child relationship"
  reason.
- `PromptViewModel` (`Paarde.Avalonia/ViewModels/`, `AddTransient`, `INavigationAware`, no
  `IConfirmNavigationAway`) owns the entire compose/run flow: folder/file pickers, `@mention`
  completion, `RunPromptAsync()` (streaming `OnTextDelta`/`OnToolActivity`, `StructuredResponseParser`,
  one retry on validation failure, `_promptResultStore.SetResult(...)`,
  `_runSessionStore.RecordRun(...)`). `OnNavigatedTo` handles a `PromptLaunchRequest` navigation
  parameter — the only existing "pre-fill and optionally auto-run a prompt" contract, used by
  `HistoryViewModel.Fork`/`Rerun` and by `MainViewModel`'s quick-prompt box and recent-run reopen.
- `TutorialViewModel` (`Paarde.Avalonia/ViewModels/`, `AddTransient`, `INavigationAware`,
  `IConfirmNavigationAway` with `HasUnsavedChanges => Files.Any(f => f.IsDirty)`) owns the
  review/apply/narrative flow: its own `OpenTabs` tab strip of `ProposedFileChangeViewModel` (each
  rendered by `TutorialDetailControl.axaml`, which embeds its own `CodeEditor` *and* the per-step
  narrative cards — checkbox/title/explanation/Apply/Explain-further/View-in-Diff/Ask-a-question),
  a master file list with tri-state selection, an apply-history log, and commands
  (`ApplySelectedAsync`, `UndoLastApplyAsync`, `RevertAllAppliedAsync`, `RefineSelectedFileAsync`,
  `SendBuildTestErrorsToClaudeAsync`, export-as-Markdown/HTML, `ReplaceFile` for refined/corrected
  swaps that update `Files`, `_promptResultStore.Files`, and `OpenTabs` together).
- `DiffsViewModel`/`DiffsView.axaml` present the same `ProposedFileChangeViewModel` data read-only
  with a view-mode toggle and jump-to-Tutorial, via a separate, reusable
  `DiffsDetailControl.axaml` (`x:DataType="coreVm:ProposedFileChangeViewModel"`, no dependency on
  `DiffsViewModel` itself). **`DiffsDetailControl` is also embedded directly in `HistoryView.axaml`**
  to preview a past, already-recorded `RunSession`'s files read-only — that usage is independent of
  the live `Diffs` page and must keep working unchanged.
- `MainViewModel`/`MainView.axaml` (`AddTransient`, `INavigationAware`) hold `RecentRuns`/
  `RecentProjects` (read fresh from `IRunSessionStore` on every navigation) and `QuickPromptText`;
  all three of its commands (`OpenRecentRun`, `OpenRecentProject`, `SendQuickPrompt`) just hand off
  to the Prompt page via `NavigationService.NavigateTo(typeof(PromptViewModel).FullName!, ...)`
  with a `PromptLaunchRequest` or a saved working-directory setting — none of this logic is unique
  to Main; it is a thin dashboard in front of capabilities Prompt/History already have.
- `ExplorerViewModel` never sets the working directory itself today — its own doc comment says the
  tree "should be browsable as soon as a folder [is] picked on the Prompt page", i.e. Explorer
  currently depends on the Prompt/Main page having run at least once. There is no "open folder"
  affordance inside Explorer itself.
- `WorkspaceLayoutSettingsKeys` (`Paarde.Avalonia/Services/`) already defines a
  per-page-and-pane persisted-width convention: `ExplorerTreeWidth`, `DiffsFileListWidth`,
  `TutorialFileListWidth`, `HistorySessionListWidth`, `HistoryFileListWidth`.
- `ShellViewModel` already establishes the "cross-page singleton panel handed in, not created
  here" pattern (`RunPanelViewModel`, `StatusBarViewModel`, `WorkbenchDockViewModel`,
  `QuickOpenViewModel`, `CommandPaletteViewModel`), plus the "`AttachX` setter after both
  singletons are resolved" pattern for breaking circular DI between two singletons
  (`QuickOpenViewModel`/`CommandPaletteViewModel`).
- `App.axaml.cs` hardcodes the startup page:
  `GetService<INavigationService>().NavigateTo(typeof(MainViewModel).FullName!);`.

## Goals

- Make the code editor the center of the screen, always visible, instead of one stop on a
  page-to-page wizard (Main → Prompt → [run] → Tutorial/Diffs).
- Dock a single "Assistant" panel beside Explorer's editor, with two tabs: **Compose** (today's
  Prompt page) and **Review** (today's Tutorial page, minus its own tab strip/embedded editor —
  review now happens directly in Explorer's own editor tabs).
- Let a finished prompt run's files open automatically as Explorer tabs, with inline diff
  rendering (Epic 13's `DiffBackgroundRenderer`/`DiffHunkGutterMargin`) and the Review panel's
  narrative open beside them — no manual navigation required between "the run finished" and "I can
  see and act on the result".
- Make Explorer (editor + Assistant panel) the app's landing page.
- Retire the Main and Diffs pages as separate navigation destinations; preserve every capability
  of theirs that is not purely redundant once Compose/Review are docked and always reachable.

## Non-goals

- No change to `ILlmRunner`, `StructuredResponseParser`, `IApplyOrchestrationService`, or any other
  run/apply/build-test backend logic — this epic is purely a presentation/layout and
  page-vs-panel-lifetime reorganization of already-working features.
- No change to Terminal, History, or Settings as separate pages — they keep their own
  `NavigationView` entries and page lifetimes.
- No new diffing, hunk-selection, or apply UI primitives — reuse `ProposedFileChangeViewModel`,
  `DiffBackgroundRenderer`, and `DiffHunkGutterMargin` exactly as they exist today.
- No attempt to let the Assistant panel run multiple prompts concurrently, or to show more than one
  run's result set at a time — out of scope, same single-active-run assumption `PromptViewModel`
  already makes today.
- No redesign of the History page itself (it keeps using `DiffsDetailControl` to preview past
  sessions read-only) beyond retargeting its Fork/Rerun navigation (R8).

## Requirements

1. **R1 — `AssistantPanelViewModel`.** Add a new `AssistantPanelViewModel`
   (`Paarde.Avalonia/ViewModels/`) composing two existing view models as properties: `Compose`
   (`PromptViewModel`) and `Review` (`TutorialViewModel`), plus a `SelectedTabIndex`
   (`[ObservableProperty]`, 0 = Compose, 1 = Review) and `ShowCompose()`/`ShowReview()` helpers.
   Registered `AddSingleton`, with `PromptViewModel`/`TutorialViewModel` promoted from
   `AddTransient` to `AddSingleton` too (same "promote to singleton because it must be reachable
   from more than one place that isn't a navigation parent/child relationship" rationale Epic 22
   already used for `TerminalViewModel`). Subscribes to `IPromptResultStore.PropertyChanged`
   (the `Tutorial` property, raised by every completed run) to call `ShowReview()` automatically.
2. **R2 — Docked panel layout.** `ExplorerView.axaml` gains a third column (tree | splitter | tab
   area | splitter | Assistant panel), via a new `AssistantPanelControl.axaml` bound to
   `ExplorerViewModel.AssistantPanel`. The panel's width is resizable and persisted (new
   `WorkspaceLayoutSettingsKeys.ExplorerAssistantPanelWidth`, same `ResizablePaneHelper` already
   used for `ExplorerTreeWidth`), and collapsible via a toolbar toggle so the editor can take the
   full remaining width when the panel isn't needed.
3. **R3 — `OpenFileTabViewModel.PendingChange`.** Add a nullable `PendingChange`
   (`ProposedFileChangeViewModel?`) property. When non-null, the tab's editor renders that file's
   inline diff and edits flow through `PendingChange.EditedContent` instead of plain `Content`.
4. **R4 — Explorer auto-opens a run's result files.** `ExplorerViewModel` subscribes to
   `_promptResultStore.Files.CollectionChanged` and, for every added `ProposedFileChangeViewModel`,
   opens or activates that file's Explorer tab (reusing `OpenOrActivateTab`) with `PendingChange`
   set — no navigation needed between a run finishing and its files being visible, diffed, and
   editable.
5. **R5 — Inline diff-aware editing in Explorer's tabs.** `ExplorerFileTabControl.axaml`'s
   `CodeEditor` binds `DiffLines`/`DiffHunks` from `PendingChange` (both tolerate null, matching
   Epic 13's existing default-nothing behavior) and wires `DiffHunkActionRequested` the same way
   `TutorialDetailControl.axaml.cs` already does, so the gutter's per-hunk Accept/Reject buttons
   work identically in Explorer as they did in Tutorial.
6. **R6 — Review panel without its own editor or tab strip.** `TutorialViewModel`'s `OpenTabs`
   (the `ProposedFileChangeViewModel` tab strip) and `TutorialDetailControl`'s embedded `CodeEditor`
   are removed. The Review panel keeps the compact file list (tri-state selection,
   `SelectionSummary`), the apply/export/refine controls, the apply-history log, and the per-file
   narrative/step cards — now rendered in a new, slimmed-down `ReviewFileDetailControl.axaml`
   containing only the step cards (no embedded editor). Selecting a file in the Review panel's list
   opens/focuses that file's Explorer tab (reusing R4's path) instead of switching an internal tab
   strip. `TutorialViewModel`'s `Selected` becomes a read of `ExplorerViewModel.SelectedTab`'s
   `PendingChange`, not an independently settable property — one selected-file state, not two
   drifting in parallel.
7. **R7 — In-panel jump between editor and narrative, no page navigation.** Replace
   `DiffJumpRequest`/`TutorialJumpRequest` cross-page navigation (`TutorialStepViewModel`'s
   "View in Diff" action, and Diffs' "back to Tutorial") with a direct method call: opening/focusing
   the Explorer tab for a file (R4's path) and switching the Assistant panel to the matching
   tab/scroll position, wired via a plain C# event (`TutorialViewModel` raises it,
   `ExplorerViewModel` subscribes once in its constructor) — not a new pub/sub context type, same
   reasoning Epic 24 used for not making Quick Open/command-palette mutual exclusion a new context
   type either.
8. **R8 — Retarget every `PromptLaunchRequest` call site.** `HistoryViewModel.Fork`/`Rerun` and
   (per R10) the surviving quick-launch affordance call `AssistantPanelViewModel.Compose`'s new
   public `LoadLaunchRequest(PromptLaunchRequest request)` method directly (R1's singleton,
   constructor-injected) and `AssistantPanelViewModel.ShowCompose()`, then
   `INavigationService.NavigateTo(typeof(ExplorerViewModel).FullName!)` so the user actually lands
   on the page where the panel lives — replacing every `NavigateTo(typeof(PromptViewModel)...)`
   call site. `PromptViewModel.OnNavigatedTo`'s `PromptLaunchRequest`-via-navigation-parameter
   handling is removed along with its `INavigationAware` implementation.
9. **R9 — Delete the Main and Diffs pages.** `MainViewModel`/`MainView.axaml[.cs]` and
   `DiffsViewModel`/`DiffsView.axaml[.cs]` (the page, not `DiffsDetailControl`, which
   `HistoryView.axaml` keeps using unchanged) are deleted, along with their `NavigationView`
   entries in `ShellView.axaml` and their `DiffJumpRequest`/`TutorialJumpRequest` record types once
   R7 removes their only usage.
10. **R10 — Explorer becomes the landing page, with its own "Open folder" entry point.**
    `App.axaml.cs`'s startup navigation targets `ExplorerViewModel` instead of `MainViewModel`.
    Since Explorer no longer has Main's folder-picker as a prerequisite step, add a small "Open
    folder…" entry point directly to Explorer's own toolbar (reusing `PromptViewModel`'s existing
    `IFolderPickerService`-backed picker logic and the same `WorkingDirectorySettingsKeys
    .WorkingDirectory` setting), so a first-time user can get a browsable tree without ever opening
    the Compose tab. `MainViewModel`'s recent-run reopen (`OpenRecentRun`) and quick-prompt box
    (`SendQuickPromptAsync`) are not re-created elsewhere: recent-run reopen is already covered by
    the History page (kept), and the Compose tab's own prompt box is reachable immediately via R2's
    always-visible panel — there is no first-launch state in which a user can see Explorer but not
    the Compose tab.
11. **R11 — `IConfirmNavigationAway` removed from `TutorialViewModel`.** Once `TutorialViewModel`
    is a singleton (R1), its state (and therefore any in-progress, unsaved review edits) survives
    navigating to other pages and back — nothing is actually destroyed, so the
    "discard unsaved edits?" guard that existed specifically to warn about page-instance
    destruction no longer protects against anything real, and is removed along with
    `INavigationAware`/`OnNavigatedFrom`. `ExplorerViewModel`'s own, unrelated
    `IConfirmNavigationAway` (guarding `OpenTabs`-level dirty content) is untouched.
12. **R12 — Persisted-layout cleanup.** Remove `WorkspaceLayoutSettingsKeys.DiffsFileListWidth` and
    `TutorialFileListWidth` (nothing reads them once R6/R9 land) and add
    `ExplorerAssistantPanelWidth` (R2).
13. **R13 — Command palette registry cleanup.** `CommandPaletteViewModel`'s hand-written registry
    (Epic 24) has one `GoTo(...)` entry per page, including `"Main"`, `"Prompt"`, `"Diffs"`, and
    `"Tutorial"` — all four targeting view model types this epic deletes (R9). Remove those four
    entries; `"Explorer"`, `"Terminal"`, `"History"`, `"Settings"` (plus `"Build"`/`"Run"`/`"Stop"`)
    are untouched.

## Open questions

Resolved in `design.md`:

- Exact shape of `ReviewFileDetailControl.axaml` versus the step-card markup
  `TutorialDetailControl.axaml` already has (how much is copy-and-trim versus a true shared
  sub-control).
- Exact mechanism for `TutorialViewModel`'s file-list selection to open/focus an Explorer tab
  without a circular constructor dependency between `ExplorerViewModel` and `AssistantPanelViewModel`.
- Whether `AssistantPanelViewModel` lives as an `ExplorerViewModel` property (visually scoped to
  Explorer only) or a `ShellViewModel` property (visually global, like the output dock) — resolved
  in favor of `ExplorerViewModel`, since nothing else in the shell needs it visible.
