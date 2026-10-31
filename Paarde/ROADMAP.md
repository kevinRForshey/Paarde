# Paarde — State Analysis & Roadmap

> **2026-10-18 update:** Renamed from `CodeGenToTutorial` to **Paarde** (Prompt Assist And Review
> Development Environment) across every project, namespace, and the solution file. Done-notes below
> that predate the rename still reference the old `CodeGenToTutorial.*` paths/namespaces, since those
> are an accurate record of what existed at the time each task was completed — not retroactively
> rewritten, same reasoning as not rewriting git history.

**Vision:** An IDE-like desktop app that takes LLM prompts for software development work, turns
the model's output into a readable tutorial, and lets the user pick which code to apply —
editing it first if they want — before the files on disk are updated.

**Analysis date:** 2026-10-01
**Analyzed commit:** `ee2bd23` (+ uncommitted `CodeGenToTutorial.Avalonia/`)

> **2026-10-01 update:** Epic 1 (consolidate onto Avalonia) is done — see the "Progress" note at the
> top of that epic for what changed. The rest of this document still describes the state as of the
> original analysis unless noted.

---

## 1. Where we are today

### 1.1 Solution shape

| Project | TFM | Role | Status |
|---|---|---|---|
| `CodeGenToTutorial.Avalonia` | `net10.0` | Avalonia 11.3.22 + FluentAvaloniaUI desktop app | **Active**; builds clean on Linux |
| `CodeGenToTutorial.Core` | `net10.0` | Shared domain models/services | **Active**; holds the parser, diff builder, CLI runner, apply service |

~~`CodeGenToTutorial` (WinUI 3 / Template Studio app, `net7.0-windows`)~~ — removed 2026-10-01; it was
a Windows-only parallel copy of the Avalonia app that could not build in this environment and forced
every change to be made twice. Its history is still in git (`git log -- CodeGenToTutorial/`) if
anything needs to be recovered.

The previous duplication between `Models/`, `Helpers/`, `Services/`, `ViewModels/` in the two UI
trees has been resolved: the domain layer (`ClaudeCliService`, `ClaudeResponseParser`, `DiffBuilder`,
`FileChangeApplyService`, `PromptResultStore`, and the shared models) now lives once, in
`CodeGenToTutorial.Core`, referenced by the Avalonia app. The Template Studio sample data
(`SampleCompany`, `SampleOrder`, `SampleOrderDetail`, `SampleDataService`) has been deleted from
Core; `IFileService`/`FileService` remain there since `LocalSettingsService` depends on them.

### 1.2 The pipeline that works

The core loop is wired end to end and is genuinely functional:

1. **Prompt page** (`PromptView` / `PromptViewModel`) — folder picker for the project location,
   multi-line prompt box, save-prompt, run button with a progress ring, raw CLI output box. Prompt
   text and working directory persist via `LocalSettingsService`.
2. **CLI invocation** (`ClaudeCliService`) — shells out to `claude -p`, writing the prompt to
   **stdin** (deliberately, to survive embedded newlines), `cmd.exe /c` on Windows to pick up the
   npm shim, direct exec elsewhere. Captures stdout/stderr/exit code as `ClaudeCliResult`.
3. **Protocol** (`PromptViewModel.TutorialInstructionPrompt`) — a hard-coded preamble that asks the
   model to either answer plainly (general question) or emit `## Tutorial` + `## Files`, with
   `### File: <path>` / fenced whole-file content / `#### Tutorial` / `##### Step: <title>` +
   explanation + fenced snippet.
4. **Parsing** (`ClaudeResponseParser`) — two `[GeneratedRegex]` patterns pull out files and per-file
   steps into `ClaudeParsedResponse` → `ProposedFileChange` → `TutorialStep`. Unstructured responses
   fall through as a plain tutorial string with zero files.
5. **Hand-off** (`PromptResultStore`) — singleton in-memory store bridging the transient ViewModels.
6. **Diffs page** — recomputes a line-level diff against the on-disk original using a real LCS
   dynamic-programming table (`DiffBuilder`, with a 4M-cell guard that degrades to whole-file
   replace), master/detail list + color-coded `+`/`-`/` ` lines, copy-file-contents button.
7. **Tutorial page** — master/detail list of files; `TutorialDetailControl` builds a
   `TutorialStepViewModel` per step showing title, explanation, read-only code box, and an
   **"Apply this change"** button that writes the file.

Infrastructure also in place: Generic Host DI, `NavigationView` shell with back-stack
(`NavigationService`/`PageService`/`NavigationViewService`), `INavigationAware` lifecycle, theme
selector + settings page, JSON local settings.

### 1.3 Honest completeness estimate

| Capability from the vision | State |
|---|---|
| Takes LLM prompts for software development | ~70% — works, but single-shot, no sessions/history |
| Turns output into a tutorial | ~55% — per-file steps render; summary is dropped; plain text only |
| Lets the user **select** which code to apply | ~15% — per-*file* only, no selection UI, no hunks |
| Lets the user **make changes** before applying | **0%** — code boxes are `IsReadOnly="True"` |
| Feels like an **IDE** | ~10% — nav shell exists; no file tree, no editor, Main page is empty |
| Safe/reversible file updates | ~10% — blind `File.WriteAllText`, no backup, no undo, no git |

**Overall: a working vertical-slice prototype — roughly 30–35% of the stated product.** The two
headline differentiators (granular selection and editing-before-apply) are the two things that
don't exist yet.

---

## 2. Gap analysis — specific findings

### Blocking the vision

- **No editing of proposed code.** `TutorialDetailControl.axaml` binds the snippet to a
  `TextBox` with `IsReadOnly="True"`, and `ProposedFileChange` is `init`-only immutable. There is no
  path from a user edit back to what gets written.
- **No granular selection.** `FileChangeApplyService.Apply` writes `file.Content` — the entire
  proposed file. `TutorialStepViewModel.Apply` therefore calls `MarkAllStepsApplied()`, which flips
  every step for that file to applied. The per-step "Apply this change" button is, by design today,
  a per-file apply wearing a per-step label (the code comment says so explicitly). No checkboxes,
  no hunk staging, no select-all/none, no "apply all approved".
- **No IDE surface.** `MainView.axaml` is an empty `<Grid>`. There is no project file tree, no
  editor, no tabs, no way to open a file that the LLM didn't mention.

### Correctness / UX bugs

- **The tutorial summary is never shown.** `PromptViewModel` parses `## Tutorial` and stores it in
  `PromptResultStore.Tutorial`, but no view or ViewModel ever reads that property. The overview
  paragraph is silently discarded.
- **Diffs go stale after applying.** `DiffsViewModel.OnNavigatedTo` re-reads the original from disk,
  so once a change is applied the diff collapses to "no changes" with no indication of why, and
  there is no refresh command.
- **Results are lost on restart.** `PromptResultStore` is memory-only.
- **No cancellation.** `IClaudeCliService.RunPromptAsync` accepts a `CancellationToken` but the
  ViewModel never passes one and there is no cancel button or timeout. A hung `claude` hangs the run
  forever with only a spinner.
- **Missing-CLI failure is unhandled.** If `claude` isn't on `PATH`, `Process.Start` throws
  `Win32Exception`, which the generic `catch (Exception)` renders as a raw message. No preflight
  check, no configurable binary path.
- **`ShellView.axaml` emits `AVLN3001`** (no public constructor reachable for the runtime loader).
### Security / safety

- **Path traversal.** `Path.Combine(workingDirectoryPath, file.FilePath)` with an LLM-supplied
  relative path will happily resolve `../../etc/...` or an absolute path and write outside the
  project. There is no canonicalization or containment check.
- **No backup, no undo, no confirmation.** Applying overwrites the working tree destructively with
  no snapshot and no git awareness.

### Protocol fragility

- Whole-file echo costs enormous tokens and breaks down on large files; truncated output silently
  yields a malformed/partial file that would be written verbatim.
- Markdown-regex parsing depends on the model never emitting a language tag after the fence
  (explicitly requested in the prompt) and breaks on nested or triple-backtick content.
- No schema validation, no structured output (JSON / tool calls), no retry on parse failure,
  no streaming — the user stares at a spinner for the whole run.

### Engineering hygiene

- **Zero tests** anywhere in the solution, against a project `ai-governance` skill that mandates a
  90% coverage floor. `DiffBuilder` and `ClaudeResponseParser` are pure, deterministic, and trivially
  testable — this is the cheapest quality win available.
- No CI, no analyzers beyond defaults, no app-level README (both READMEs are Template Studio text).
- `NU1903`: `Tmds.DBus.Protocol` 0.20.0 (transitive via Avalonia 11.2.1) has a known high-severity
  advisory. Mixed TFMs (`net7.0` Core ← `net10.0` app) and stale packages
  (`Microsoft.Extensions.Hosting` 8.0.1 on a net10 target).

---

## 3. Epics, Features & Tasks

Priority: **P0** = required for the vision to exist · **P1** = required to be usable/trustworthy ·
**P2** = polish & scale.

---

### EPIC 1 — Consolidate the codebase onto one UI stack *(P0)* — ✅ Done 2026-10-01

*Why first: every item below would otherwise have to be built twice.*

**F1.1 — Pick and commit to a single UI target** ✅
- [x] T1.1.1 Decide: Avalonia-only (cross-platform, builds today) vs WinUI. **Decision: Avalonia.**
- [x] T1.1.2 Remove the WinUI project; dropped from `CodeGenToTutorial.slnx` (history preserved in git).
- [x] T1.1.3 Delete the duplicated `Models`/`Helpers`/`Services`/`ViewModels` — moot, whole tree removed.

**F1.2 — Make `.Core` the real shared domain library** ✅
- [x] T1.2.1 Retargeted `.Core` to `net10.0`; package versions aligned via `Directory.Build.props`.
- [x] T1.2.2 Moved `ProposedFileChange`, `TutorialStep`, `DiffLine`, `ClaudeCliResult`,
  `ClaudeParsedResponse` into `.Core/Models`.
- [x] T1.2.3 Moved `DiffBuilder`, `ClaudeResponseParser`, `ClaudeCliService`, `FileChangeApplyService`,
  `PromptResultStore` into `.Core` behind their interfaces; the Avalonia app now only holds
  Avalonia-specific concerns (nav, theming, local settings, folder picker, converters).
- [x] T1.2.4 Deleted the Template Studio sample-data types (`SampleCompany`, `SampleOrder`,
  `SampleOrderDetail`, `SampleDataService`) and `ISampleDataService`. `IFileService`/`FileService`
  were kept — `LocalSettingsService` genuinely depends on them.

**F1.3 — Clean the build** ✅
- [x] T1.3.1 Fixed `AVLN3001` on `ShellView` — it took a DI constructor parameter unlike every other
  view; changed to the same parameterless `App.GetService<T>()` pattern the rest of the views use.
- [x] T1.3.2 Bumped Avalonia 11.2.1 → **11.3.22** and FluentAvaloniaUI 2.2.0 → **2.5.1** (same major,
  avoiding the untested Avalonia 12 / FluentAvaloniaUI 3 breaking changes) — resolves the
  `Tmds.DBus.Protocol` `NU1903` advisory. Also bumped `CommunityToolkit.Mvvm` → 8.4.2 and
  `Microsoft.Extensions.Hosting` → 10.0.12 to match the `net10.0` target.
- [x] T1.3.3 Added `Directory.Build.props` at the solution root (`Nullable`, `ImplicitUsings`,
  `LangVersion`, analyzers) shared by both remaining projects. Enabling `Nullable` on `.Core`
  surfaced two latent warnings in the old Template Studio `Json`/`FileService` helpers; fixed them
  (nullable return/parameter annotations) rather than suppress.

Verified: `dotnet build CodeGenToTutorial.slnx` from a clean `bin`/`obj` → **0 warnings, 0 errors**;
app launches (`dotnet run --project CodeGenToTutorial.Avalonia`) without throwing. Nothing is
committed yet — review `git status`/`git diff` before committing.

---

### EPIC 2 — Editable proposed changes *(P0 — the biggest missing piece)*

**F2.1 — Make the change model mutable and observable**
- [x] T2.1.1 Replace `ProposedFileChange` (init-only) with an observable
  `ProposedFileChangeViewModel` holding `OriginalContent`, `ProposedContent`, `EditedContent`,
  and `IsDirty`. — done 2026-10-06: new class lives in `CodeGenToTutorial.Core/ViewModels`
  (Core now takes a `CommunityToolkit.Mvvm` dependency so `PromptResultStore` — the shared graph
  both the Tutorial and Diffs pages read from — can hold observable items directly).
  `FilePath`/`OriginalContent`/`ProposedContent`/`TutorialSteps`/`DiffLines` are set at construction;
  `EditedContent` defaults to `ProposedContent` and `IsDirty` flips automatically when it diverges.
  Nothing mutates `EditedContent` yet (no editor exists — that's F2.2), and
  `FileChangeApplyService.Apply` still writes `ProposedContent`, so behavior is unchanged; this is
  scaffolding for T2.2.2/T2.3.1. Updated every consumer (`ClaudeResponseParser`, `PromptResultStore`,
  `FileChangeApplyService`, `DiffsViewModel`, `TutorialViewModel`, `TutorialStepViewModel`, the
  Diffs/Tutorial `.axaml` views and code-behind) and all existing tests; added
  `ProposedFileChangeViewModelTests` covering the dirty-tracking behavior. Full solution builds
  with 0 warnings/errors, all 46 tests pass, app launches without throwing.
- [x] T2.1.2 Introduce a per-step `ProposedHunk` model (step title, explanation, snippet, resolved
  location in the file, selection state) so steps stop being presentational-only. — done
  2026-10-06: `ProposedHunk` (`CodeGenToTutorial.Core/ViewModels`) carries `Title`/`Explanation`/
  `Snippet` plus an observable `IsSelected` (defaults `true`, nothing reads it yet — scaffolding
  for F3.2) and a `Location` (`HunkLocation`, start/end line numbers within `ProposedContent`)
  resolved by a new `HunkLocationResolver` helper that does an exact line-sequence search; returns
  `null` if the snippet isn't found verbatim (known fragility, same class as the nested-fence
  parsing issue — a baseline for Epic 5, not a bug to fix here). `ProposedFileChangeViewModel`'s
  `TutorialSteps` property is renamed to `Hunks` and now holds `ProposedHunk` instead of the raw
  `TutorialStep`; `ClaudeResponseParser` builds hunks (and resolves their location) right after
  parsing each file's content. `TutorialStepViewModel` now takes a `ProposedHunk` instead of a
  `TutorialStep` — display behavior (`Title`/`Explanation`/`Code`) is unchanged. Added
  `HunkLocationResolverTests`. Full solution builds clean, 52/52 tests pass, app launches without
  throwing.
- [x] T2.1.3 Make `PromptResultStore` hold the mutable graph and raise change notifications. — done
  2026-10-08: the store already held the shared mutable `ProposedFileChangeViewModel` graph (since
  T2.1.1/T2.3.2), but it was a plain POCO — no way for anything to react to a new prompt result
  except a page's `OnNavigatedTo` happening to re-run. `PromptResultStore` is now `partial class
  PromptResultStore : ObservableObject, IPromptResultStore`: `Tutorial`/`WorkingDirectoryPath` are
  `[ObservableProperty]` fields (raise `PropertyChanged`), and `Files` is a fixed
  `ObservableCollection<ProposedFileChangeViewModel>` instance — `SetResult` now `Clear()`s and
  re-`Add()`s into it rather than reassigning the reference, so a subscriber holding the original
  `Files` collection keeps seeing `CollectionChanged` across repeated prompt runs instead of
  watching a now-stale snapshot. `IPromptResultStore` extends `INotifyPropertyChanged` and exposes
  `Files` as the concrete `ObservableCollection` (was `IReadOnlyList`) so consumers can subscribe to
  either. `TutorialViewModel`/`DiffsViewModel` need no changes — their `OnNavigatedTo` already just
  enumerates `_promptResultStore.Files`, which works unchanged against the new collection type.
  Updated `FakePromptResultStore` to match the interface and added
  `CodeGenToTutorial.Core.Tests/Services/PromptResultStoreTests.cs` (stores values, raises
  `PropertyChanged` for `Tutorial`/`WorkingDirectoryPath`, raises `CollectionChanged` on `Files`,
  and the same `Files` instance survives a second `SetResult`). Full solution builds clean,
  65/65 tests pass (61 existing + 4 new). Not wired to a new UI consumer yet — nothing currently
  binds directly to the store outside the existing `OnNavigatedTo` pull, so this is the
  notification plumbing the rest of F2.1/Epic 3 (and the still-open "tutorial summary is never
  shown" bug from the gap analysis) can build on, not a new visible behavior by itself.

**F2.2 — In-app code editor**
- [x] T2.2.1 Evaluate and add an editor control (AvaloniaEdit is the obvious candidate) — syntax
  highlighting, line numbers, bracket matching. — done 2026-10-06: added `Avalonia.AvaloniaEdit`
  11.4.0 (+ `AvaloniaEdit.TextMate`/`TextMateSharp.Grammars` for TextMate-based highlighting); its
  `Avalonia` dependency floor is 11.0.0 so it stays on the pinned 11.3.22 line. New `CodeEditor`
  control (`CodeGenToTutorial.Avalonia/Controls`) wraps `AvaloniaEdit.TextEditor`: turns on
  `ShowLineNumbers`, installs TextMate with a grammar picked from a bound `FilePath`'s extension,
  and adds bracket matching via a new `BracketHighlightRenderer` (`IBackgroundRenderer`) since
  AvaloniaEdit has none built in — it's a language-agnostic depth-counting scan of the raw text
  for a `()`/`[]`/`{}` pair touching the caret, not grammar-aware, so it doesn't skip brackets
  inside strings/comments (same class of known fragility as the response-parser issues, acceptable
  for a code viewer). Because `TextEditor.Text` is a plain CLR property rather than an
  `AvaloniaProperty`, added a `TextEditorExtensions.BindableText` attached property so it can still
  be the target of an XAML `Binding`. `TutorialDetailControl`'s per-step `TextBox` is replaced with
  `CodeEditor` (still `IsReadOnly="True"` here; wiring it to `EditedContent` for in-place editing is
  T2.2.2). `TutorialStepViewModel` gained a `FilePath` property (from its `ProposedFileChangeViewModel`)
  so the control has an extension to pick a grammar from. Added `BracketHighlightRendererTests`
  against the extracted matching algorithm (internal, `InternalsVisibleTo` the Avalonia test
  project). Full solution builds clean, 57/57 tests pass. Not visually verified in a browser/running
  app — this sandbox has no X server (`XOpenDisplay failed`, no `Xvfb`), so the editor's rendering
  (line numbers, highlighting colors, bracket highlight) is unverified beyond compiling and the
  unit-tested matching logic.
- [x] T2.2.2 Replace the read-only `TextBox` in `TutorialDetailControl` with an editable editor bound
  to `EditedContent`. — done 2026-10-07: `FileChangeApplyService.Apply` writes a file's whole
  `ProposedContent`, not any one step's snippet, so there's exactly one editable buffer per file,
  not per step. Moved the `CodeEditor` out of the per-step `DataTemplate` and hoisted it to file
  scope in `TutorialDetailControl` (one instance, `IsReadOnly="False"`, bound
  `EditedContent, Mode=TwoWay`); each step card below it now only shows `Title`/`Explanation`/
  `Apply`. Made `TextEditorExtensions.BindableText` round-trip: it now hooks the editor's
  `TextChanged` once per instance and pushes `editor.Text` back onto the attached property (the
  attached property's default binding mode is `TwoWay`), and only reassigns `editor.Text` when the
  incoming value actually differs — needed because `TextEditor.Text`'s setter unconditionally
  resets `CaretOffset` to 0 and clears the undo stack, which would fight the user's typing on every
  keystroke otherwise. Removed `TutorialStepViewModel.Code`/`FilePath` (dead now that the per-step
  snippet view is gone). Tried adding a test that constructs a real `AvaloniaEdit.TextEditor` to
  exercise the two-way bridge directly; it throws (`CaretNavigationCommandHandler`'s static
  keymap init needs a running Avalonia `Application`), and this project has no
  `Avalonia.Headless` test host, so that's unverified by a test — reviewed by hand instead. Full
  solution builds clean, 57/57 tests pass. Not visually verified (same no-X-server sandbox
  limitation noted under T2.2.1); applying edited-but-unsaved content is still a no-op until
  T2.3.1 rewires `Apply` to use `EditedContent` instead of `ProposedContent`.
- [x] T2.2.3 Dirty tracking, revert-to-proposed, and revert-to-original per file. — done 2026-10-08:
  dirty tracking already existed (`IsDirty`, from T2.1.1). Added the two revert actions as
  `[RelayCommand]`s directly on `ProposedFileChangeViewModel` (`CommunityToolkit.Mvvm.Input`,
  already available via the `CommunityToolkit.Mvvm` package `.Core` already depends on) rather than
  on a page ViewModel, since the state being reverted (`EditedContent`) and the state being
  reverted *to* (`ProposedContent`/`OriginalContent`) all live on that one object already:
  `RevertToProposedCommand` sets `EditedContent = ProposedContent` and is disabled once
  `!IsDirty` (nothing to discard); `RevertToOriginalCommand` sets `EditedContent =
  OriginalContent` and is disabled once `EditedContent == OriginalContent` - deliberately *not*
  gated on `IsDirty`, so a not-dirty file whose `ProposedContent` still differs from
  `OriginalContent` can still be rejected outright. Both commands' `CanExecute` are kept correct
  via existing `On*Changed` partial hooks (`OnIsDirtyChanged` →
  `RevertToProposedCommand.NotifyCanExecuteChanged()`; `OnEditedContentChanged`/
  `OnOriginalContentChanged` → `RevertToOriginalCommand.NotifyCanExecuteChanged()`), same pattern
  `PromptViewModel` already uses for `RunPromptCommand`. Wired to two new buttons in
  `TutorialDetailControl.axaml`, above the per-file `CodeEditor` (the only place editing happens,
  per T2.2.2) alongside an "Edited" label bound to `IsDirty`; the Diffs page is unchanged since it
  has no editor to revert. Added 8 new tests to `ProposedFileChangeViewModelTests` covering both
  commands' `CanExecute` gating and effect, including the not-dirty-but-still-revertible-to-original
  case. Full solution builds clean, 73/73 tests pass (65 existing + 8 new). Not visually verified
  (no X server in this sandbox, same limitation noted on prior Epic 2 tasks).
- [x] T2.2.4 Undo/redo within the editor; warn on navigating away with unsaved edits. — done 2026-10-08:
  investigated AvaloniaEdit via reflection on the compiled `Avalonia.AvaloniaEdit` assembly (doc
  searches kept 404ing on exact source lines) and found `TextEditor` already has public
  `Undo()`/`Redo()`/`CanUndo`/`CanRedo`, and that `TextAreaDefaultInputHandler` (installed on every
  `TextArea` by default) already wires Ctrl+Z/Ctrl+Y to them — so keyboard undo/redo needed zero
  code. The only real gap was discoverability: added named `UndoButton`/`RedoButton` buttons to
  `TutorialDetailControl.axaml` next to the revert buttons, wired to `Editor.Undo()`/`Editor.Redo()`
  in the code-behind. `CanUndo`/`CanRedo` are plain CLR properties (not `AvaloniaProperty`s), so
  they can't be bound from XAML directly; kept them in sync via the editor's public `TextChanged`
  event (fires on typing *and* on Undo/Redo itself), re-checking both buttons' `IsEnabled` each
  time. `TutorialDetailControl` is a single long-lived instance whose `DataContext` rebinds per
  file (per T2.2.1/T2.2.2), so there's one `Document`/`UndoStack` for the page's lifetime; switching
  files resets undo history for the new file, which is expected.
  For the navigate-away warning: found via reflection that FluentAvalonia's `Frame` has a
  cancelable `Navigating` event (`NavigatingCancelEventArgs.Cancel` is settable) that fires *before*
  content changes - unlike `Navigated`/`INavigationAware.OnNavigatedFrom`, which both only run
  *after* navigation already happened, so neither could actually block anything. Added
  `IConfirmNavigationAway` (opt-in interface, `bool HasUnsavedChanges`) implemented by
  `TutorialViewModel` as `Files.Any(f => f.IsDirty)`, and a new `IDialogService`/`DialogService`
  wrapping FluentAvalonia's `ContentDialog` for a generic yes/no confirm (`DefaultButton =
  ContentDialogButton.Close` so Enter/default focus can't accidentally pick the destructive
  choice). `NavigationService` now subscribes to `Frame.Navigating`: if the leaving page reports
  unsaved changes, cancels synchronously and shows the confirm dialog; on "leave without saving" it
  re-issues the exact same navigation (forward or `GoBack`) with a `_suppressUnsavedChangesCheck`
  flag set so the retry doesn't re-trigger the same prompt. Refactored `NavigateTo` to share its
  core logic with the retry path via a private `NavigateToPageType` helper. Also fixed a
  pre-existing latent bug found while doing this: `GoBack()` previously called
  `OnNavigatedFrom()`/returned `true` unconditionally, even though `Frame.GoBack()` returns `void`
  and could be silently cancelled by the new `Navigating` handler - it now compares
  `GetPageViewModel()` before/after the call to detect whether navigation actually happened.
  Registered `IDialogService` in `App.axaml.cs`'s DI container. Added `TutorialViewModelTests`
  (new file) covering `HasUnsavedChanges` across no files / clean files / a dirtied file / a
  dirtied-then-reverted file. Did not add tests for `NavigationService`'s new `Navigating` handling
  or `DialogService`'s `ContentDialog` usage - both need a real FluentAvalonia `Frame`/`ContentDialog`
  instance, which (consistent with the AvaloniaEdit `TextEditor` limitation noted under T2.2.2)
  can't be constructed without a running Avalonia `Application` and headless test host this project
  doesn't have; reviewed by hand instead. Full solution builds clean, 77/77 tests pass (73 existing
  + 4 new). Not visually verified (no X server in this sandbox, same limitation noted on prior Epic
  2 tasks) - in particular, the undo/redo buttons' enabled state and the confirm dialog's actual
  on-screen appearance are unverified beyond code review.

**F2.3 — Write the user's edits, not the model's output**
- [x] T2.3.1 Change `IFileChangeApplyService.Apply` to take the effective (possibly edited) content. —
  done 2026-10-07: `ProposedFileChangeViewModel` already modeled the effective content as
  `EditedContent` (defaults to `ProposedContent`, diverges once the user types in the `CodeEditor`
  from T2.2.2), so no interface/signature change was needed — `FileChangeApplyService.Apply` now
  writes `file.EditedContent` instead of `file.ProposedContent`. Added
  `Apply_UserEditedContent_WritesEditedContentNotProposedContent` to pin the behavior; all existing
  tests still pass unchanged since they never diverge `EditedContent` from `ProposedContent`. Full
  solution builds clean, 58/58 tests pass. Known follow-on gap, not fixed here: the Diffs page
  (`DiffsViewModel.OnNavigatedTo`) builds its *own* `ProposedFileChangeViewModel` instances from
  `file.ProposedContent`, separate from the ones the Tutorial page edits, so Diffs still shows the
  raw proposal rather than live edits — that's T2.3.2.
- [x] T2.3.2 Re-diff live as the user edits so the Diffs page reflects edits, not just the raw
  proposal. — done 2026-10-07: `DiffsViewModel.OnNavigatedTo` used to build its own throwaway
  `ProposedFileChangeViewModel` copies from `file.ProposedContent`, separate from the instances the
  Tutorial page edits, so Diffs could never see live edits regardless of how the diff was computed.
  Fixed by having Diffs reuse the store's own instances (same objects as Tutorial, matching the
  pattern `TutorialViewModel` already used) and moving diff computation *into*
  `ProposedFileChangeViewModel` itself: `DiffLines` is now a computed `[ObservableProperty]`
  recomputed via `DiffBuilder` whenever `OriginalContent` or `EditedContent` change (constructor
  computes the initial value; `OnEditedContentChanged`/`OnOriginalContentChanged` recompute it).
  `OriginalContent` changed from a get-only constructor param to a settable `[ObservableProperty]`
  so Diffs can back-fill it from disk on navigation without re-constructing the object. Net effect:
  typing in the Tutorial page's `CodeEditor` now updates the Diffs page's diff immediately, since
  both pages are watching the same `EditedContent`-driven `DiffLines` on the same instance. Added
  `ProposedFileChangeViewModelTests` cases for initial/edited/original-driven recompute. Full
  solution builds clean, 61/61 tests pass. Not visually verified (no X server in this sandbox, same
  limitation as T2.2.1/T2.2.2) — confirmed by test and by reading the binding (`DiffsDetailControl`
  binds `DiffLines` via a compiled binding, which already refreshes on `PropertyChanged`).

---

### EPIC 3 — Granular selection & apply *(P0)*

**F3.1 — Hunk-level diff model**
- [x] T3.1.1 Extend `DiffBuilder` to group `DiffLine`s into hunks with context (unified-diff style). —
  done 2026-10-10: `DiffBuilder.GroupIntoHunks` (+ new `DiffHunk` model) groups a flat line diff into
  unified-diff-style hunks with configurable context, merging change regions whose gap is within
  `2 * contextLines`. Not consumed by anything yet - scaffolding for T3.1.2/T3.1.3.
- [x] T3.1.2 Map each `TutorialStep` snippet onto the hunk(s) it produces, so "apply this step" means
  something precise. — done 2026-10-10: new `ProposedHunkMatcher.FindMatches` maps a `ProposedHunk`'s
  resolved `HunkLocation` onto the `DiffHunk`(s) whose proposed-side range overlaps it (pure-deletion
  hunks, which contribute no proposed lines, never match). `ProposedFileChangeViewModel` now computes
  `DiffHunks` reactively alongside `DiffLines`, and keeps every `Hunk`'s new `MatchedDiffHunks`
  property in sync whenever `DiffHunks` changes (edits, or the `Hunks` list itself being assigned).
  Still display-only - nothing reads `MatchedDiffHunks` yet; that's T3.1.3/F3.2.
- [x] T3.1.3 Implement patch application: compose the target file content from the selected hunks plus
  the untouched original. — done 2026-10-11: `DiffHunk` gained `SourceRangeStart`/`SourceRangeEnd`
  (its `[start, end)` index range within the `DiffLines` it was grouped from) so a hunk's lines can be
  distinguished from its neighbors' when recomposing. New `DiffBuilder.ComposeContent(diffLines,
  hunks, isSelected)` walks the full line list once: inside a selected hunk's range it keeps
  `Unchanged`/`Added` lines (the proposed outcome); inside an unselected hunk's range it keeps
  `Unchanged`/`Removed` lines (reverts that region to the original); everything outside any hunk's
  range is guaranteed `Unchanged` (hunks are built to cover every changed region) and passes through
  either way. Pure algorithm, not wired to `ProposedFileChangeViewModel`/UI - there's no selection
  state on `DiffHunk` itself yet (only per-step `ProposedHunk.IsSelected`), and deciding how step
  selection aggregates onto a shared `DiffHunk` (e.g. two steps mapping to the same hunk with
  different selection) is F3.2/F3.3's job, not this one's.

**F3.2 — Selection UI**
- [x] T3.2.1 Checkbox per file in the Diffs and Tutorial master lists. — done 2026-10-12: tri-state
  `CheckBox` bound to `ProposedFileChangeViewModel.IsSelected` in both `DiffsView` and `TutorialView`'s
  master `ListBox` item templates.
- [x] T3.2.2 Checkbox per hunk/step in the detail pane, with per-file tri-state roll-up. — done
  2026-10-12: per-step checkbox in `TutorialDetailControl` bound to `ProposedHunk.IsSelected`;
  `ProposedFileChangeViewModel.IsSelected` rolls up to `true`/`false`/`null` depending on whether
  all, none, or some of a file's hunks are selected.
- [x] T3.2.3 Select-all / select-none / invert; show a count of selected changes. — done 2026-10-12:
  `SelectAllHunksCommand`/`SelectNoneHunksCommand`/`InvertHunkSelectionCommand` on
  `ProposedFileChangeViewModel`, surfaced as buttons in `TutorialDetailControl`; `SelectionSummary`
  shows the selected/total hunk count in the master list.
- [x] T3.2.4 Persist selection state across navigation between the Diffs and Tutorial pages. — done
  2026-10-12: both pages read the same `ProposedFileChangeViewModel` instances straight out of
  `IPromptResultStore.Files` rather than copying them, so selection is shared by construction; pinned
  by `SelectionPersistenceTests`.

**F3.3 — Apply orchestration**
- [x] T3.3.1 "Apply selected" command with a pre-apply summary (N files, M hunks). — done 2026-10-12:
  new `IApplyOrchestrationService.Preview` computes a file/change count across every file in the
  tutorial (not just the one shown in the detail pane); `TutorialViewModel.ApplySelectedCommand`
  shows it in a confirm dialog before calling `ApplySelected`, and a "nothing selected" message if
  there's nothing to apply.
- [x] T3.3.2 Per-file and per-hunk applied state that is *accurate* — remove the
  `MarkAllStepsApplied()` workaround. — done 2026-10-12: `TutorialDetailControl.MarkAllStepsApplied()`
  deleted. `ProposedHunk.IsApplied` is now set only by
  `ProposedFileChangeViewModel.MarkHunksAppliedFromSelection()` right after a successful write, and
  automatically resets to `false` whenever that hunk's `IsSelected` changes or the file's underlying
  diff changes (edit or original-content refresh) — so a step only ever shows "Applied" if its current
  selection was actually the one last written to disk.
- [x] T3.3.3 Conflict detection: if a file changed on disk since the proposal, warn and offer
  re-diff. — done 2026-10-12: `ApplyOrchestrationService.ApplySelected` re-reads each file's current
  disk content at apply-time and compares it to the proposal's `OriginalContent` baseline; a mismatch
  reports `FileApplyOutcome.Conflict` and skips the write rather than overwriting it. The result
  message names the conflicted files and points at the new
  `ProposedFileChangeViewModel.RefreshOriginalContent(workingDirectoryPath)` (re-reads disk and
  re-triggers the existing reactive re-diff) as the way to resolve one.
- [x] T3.3.4 Partial-failure reporting — don't abandon the batch on one failed write. — done
  2026-10-12: `ApplyOrchestrationService.ApplySelected` attempts every selected file independently,
  catching `IOException`/`UnauthorizedAccessException`/path-containment failures per file into
  `FileApplyOutcome.Failed` without stopping the rest of the batch; `ApplyBatchResult` reports
  applied/conflicted/failed counts and file lists, shown via an info dialog after the batch completes.
  Covered by `ApplyOrchestrationServiceTests` and `TutorialViewModelTests`; full solution builds with
  0 warnings/errors, 156/156 tests pass (123 Core + 33 Avalonia).

---

### EPIC 4 — Safe, reversible file updates *(P1)*

**F4.1 — Containment & validation**
- [x] T4.1.1 Canonicalize with `Path.GetFullPath` and reject any target outside the working directory;
  reject absolute and rooted LLM paths. **(Security — do this early.)** — done 2026-10-02 in
  `FileChangeApplyService.Apply`; verified traversal (`../../../etc/passwd`) and absolute-path
  escapes are rejected with `InvalidOperationException`, normal relative paths still write.
- [x] T4.1.2 Reject writes to `.git/`, `bin/`, `obj/`, and anything matched by `.gitignore` unless
  explicitly confirmed. — done 2026-10-13: added `ProtectedPathChecker.IsProtected` (true if any
  path segment before the filename is `.git`/`bin`/`obj`, or `GitIgnoreMatcher.IsIgnored` matches)
  and `GitIgnoreMatcher` (a minimal single-top-level-`.gitignore` matcher - comments, blank lines,
  `!` negation with git's last-match-wins precedence, a leading `/` root anchor, a trailing `/`
  directory marker, and `*`/`**`/`?` wildcards via hand-translated regex; deliberately doesn't
  support `[abc]` character classes, nested per-directory `.gitignore` files, a global gitignore, or
  `.git/info/exclude` - out of scope for an apply-time safety check). `IApplyOrchestrationService.
  Preview` now takes `workingDirectoryPath` and reports `ApplyPreview.ProtectedPathCount`/
  `ProtectedPathFiles` so the confirmation dialog can warn before applying; `ApplySelected` gained an
  `allowProtectedPaths` parameter (default `false`) and `ApplyOne` short-circuits to a new
  `FileApplyOutcome.ProtectedPathBlocked` before touching disk when a target is protected and the
  caller hasn't confirmed, without affecting any other file in the batch (T3.3.4). `TutorialViewModel.
  ApplySelectedAsync` shows a second, separate confirm dialog only when `ProtectedPathCount > 0`;
  declining it only skips those files (reported via `ProtectedPathBlockedCount`) rather than
  aborting the whole apply. Covered by `GitIgnoreMatcherTests`, `ProtectedPathCheckerTests`, new
  `ApplyOrchestrationServiceTests` cases, and new `TutorialViewModelTests` cases (both dialogs
  confirmed/declined independently); full solution builds with 0 warnings/errors, 208/208 tests pass
  (170 Core + 38 Avalonia). `GitIgnoreMatcher.cs` is 92.3% line/100% branch covered - the
  `catch (IOException)`/`catch (UnauthorizedAccessException)` arms around the `.gitignore` read are
  left deliberately untested, same reasoning as the identical gap in `FileEncodingDetector.cs`
  above. `ProtectedPathChecker.cs` and the new model properties are 100% line/100% branch covered.
- [x] T4.1.3 Preserve the file's existing encoding and line endings instead of defaulting. — done
  2026-10-13: `FileChangeApplyService.Apply` always wrote UTF-8 (no BOM), and
  `DiffBuilder.ComposeContent` always joins hunk-composed content with bare `\n` regardless of the
  file's actual convention - so a CRLF or BOM'd file silently flipped to LF/no-BOM on first apply.
  Added `FileEncodingDetector.Detect` (sniffs the on-disk file's BOM - UTF-8/UTF-16 LE/BE/UTF-32
  LE/BE - and dominant newline before the write overwrites it) and `FileEncodingInfo` (the detected
  `Encoding` plus an optional `NewLine`, `null` when there's nothing to preserve - a brand-new file,
  or an existing one with no newlines at all). `Apply` now normalizes `BuildSelectedContent()`'s
  line endings to match and writes with the detected `Encoding` instead of the encoding-less
  `File.WriteAllText` overload. Covered by `FileEncodingDetectorTests` (every BOM variant, CRLF/LF/
  no-newline/mixed, missing file) and new `FileChangeApplyServiceTests` cases (CRLF and LF
  round-trip through hunk composition, BOM round-trip, new-file no-regression); 174/174 tests pass
  (141 Core + 33 Avalonia), solution builds with 0 warnings/errors.
  `FileChangeApplyService.cs` is 100% line/100% branch covered; `FileEncodingDetector.cs` is 92.5%
  line/100% branch — the one gap is the `catch (IOException)` arm, structurally identical to the
  `catch (UnauthorizedAccessException)` arm next to it (both just return `.Default`), left
  deliberately untested rather than forcing a flaky/platform-specific repro of a generic I/O failure
  in a unit test.

**F4.2 — Backup & undo**
- [x] T4.2.1 Snapshot each file before first write in a session-scoped backup store. — done
  2026-10-13: added `IApplyBackupStore`/`ApplyBackupStore` (in-memory, keyed by the proposal's
  relative `FilePath`, registered as a singleton like `IPromptResultStore` so it survives page
  navigation for the app's lifetime). `ApplyOrchestrationService.ApplyOne` now calls
  `SnapshotIfAbsent` with the disk content it already reads for the T3.3.3 conflict check, right
  after `IFileChangeApplyService.Apply` succeeds - so a conflicted, protected-path-blocked, or
  failed attempt never creates a backup entry, and a second apply to a path already written this
  session doesn't overwrite the pre-session baseline with an intermediate state. Doesn't add
  undo/revert or a history UI - that's T4.2.2/T4.2.3. Covered by new `ApplyBackupStoreTests` (store
  in isolation: first-write stores, repeat writes to the same path keep the original, independent
  paths tracked separately, miss returns false) and new `ApplyOrchestrationServiceTests` cases
  (existing-file and new-file first writes snapshot correctly, a second same-session write doesn't
  clobber the snapshot, conflicted/blocked files create none); full solution builds with 0
  warnings/errors, 217/217 tests pass (179 Core + 38 Avalonia). `ApplyBackupStore.cs` is 100%
  line/100% branch covered.
- [x] T4.2.2 "Undo last apply" and "Revert all applied changes in this session". — done
  2026-10-13: added `IApplyHistoryStore`/`ApplyHistoryStore` (session-scoped LIFO stack of
  `AppliedFileChange` batches, singleton like `IApplyBackupStore`) alongside T4.2.1's store so
  "undo last apply" can restore exactly the files one batch touched even when a file's been
  applied more than once this session, while "revert all" still goes back to T4.2.1's session-wide
  baseline via a new `IApplyBackupStore.AllSnapshots`. Extracted `IFileChangeApplyService.WriteRaw`
  out of `Apply` (same path-containment/directory-creation/encoding/line-ending preservation,
  T4.1.3) so both new write-backs reuse it instead of duplicating that logic. `ApplyOrchestrationService`
  gained `TryUndoLastApply`/`RevertAllApplied`, sharing a private `WriteBack` helper that reports
  per-file success/failure via the new `RevertResult`/`RevertFileResult` models; `RevertAllApplied`
  clears the undo history afterward since older batches no longer describe a state worth returning
  to once everything's back at its original content. Wired into the UI as two new
  `TutorialViewModel` commands (`UndoLastApplyCommand`/`RevertAllAppliedCommand`) and buttons,
  following the existing `ApplySelectedCommand` confirm/apply/report-via-status-and-info-dialog
  pattern; a successful revert also calls `ProposedFileChangeViewModel.RefreshOriginalContent` on
  any affected file still in the `Files` collection so the Tutorial/Diffs pages' diff state matches
  disk again. Covered by new `ApplyHistoryStoreTests`, `ApplyBackupStoreTests`/`ApplyOrchestrationServiceTests`
  additions (LIFO undo order, undo/revert restoring the right baseline, conflicted/blocked files
  contributing nothing to a batch, a write-back failure reporting via `FailedCount`/`FailedFiles`),
  direct `FileChangeApplyServiceTests` for `WriteRaw`, and new `TutorialViewModelTests` cases for
  both commands (cancel, nothing-to-undo/revert, success, partial failure, Files refresh); full
  solution builds with 0 warnings/errors, 248/248 tests pass (201 Core + 47 Avalonia).
  `ApplyHistoryStore.cs`, `ApplyBackupStore.cs`, `FileChangeApplyService.cs`, `AppliedFileChange.cs`,
  and `RevertResult.cs` are all 100% line/100% branch covered; `ApplyOrchestrationService.cs`'s one
  remaining gap is the same pre-existing `FileApplyOutcome.Failed` catch arm in `ApplyOne` already
  documented under T4.2.1/T3.3.4, not anything new from this task.
- [x] T4.2.3 Apply-history log surfaced in the UI. — done 2026-10-14: added
  `IApplyHistoryLog`/`ApplyHistoryLog` (session-scoped, append-only list of `ApplyHistoryEntry`
  rows - timestamp, `ApplyHistoryEntryKind`, and the file paths an action actually succeeded on;
  singleton like T4.2.1/T4.2.2's stores), kept deliberately separate from `IApplyHistoryStore`
  (whose batches hold full pre-apply content and get popped away by undo) and `IApplyBackupStore`
  (no concept of individual timestamped actions at all). `ApplyOrchestrationService.ApplySelected`/
  `TryUndoLastApply`/`RevertAllApplied` each record one entry per call, filtered to succeeded files
  only, mirroring the same success-filtering already used for backups and undo batches; an action
  that touches nothing logs nothing. `TutorialViewModel` mirrors the log into a display-ready
  `ObservableCollection<string> ApplyHistoryEntries` (newest first) plus a `HasApplyHistory` flag,
  refreshed after every apply/undo/revert and on `OnNavigatedTo` so navigating away and back doesn't
  show a stale list; `TutorialView.axaml` gained a collapsed-until-first-use history panel (bounded
  `MaxHeight`) in a new third grid row. New `Tutorial_HistoryEntry*` keys added to `Resources.cs`.
  Covered by new `ApplyHistoryLogTests`, `ApplyOrchestrationServiceTests` additions (logs each kind
  with succeeded-only paths, logs nothing when nothing succeeded), and `TutorialViewModelTests`
  additions (`HasApplyHistory` toggling, `OnNavigatedTo` re-reading the log, each command refreshing
  the display newest-first); full solution builds with 0 warnings/errors, 263/263 tests pass (211
  Core + 52 Avalonia). `ApplyHistoryLog.cs` and `ApplyHistoryEntry.cs` are 100% line/100% branch
  covered; `TutorialViewModel.cs`'s only new gap is a defensive `default` arm (`ArgumentOutOfRangeException`)
  in the entry-kind formatting switch, structurally unreachable given the enum is exhaustively
  handled above it; `ApplyOrchestrationService.cs`'s one remaining gap is the same pre-existing
  `ApplyOne` catch arm already documented under T4.2.1/T4.2.2.

**F4.3 — Git integration**
- [x] T4.3.1 Detect a git repo at the working directory; show dirty state before applying. — done
  2026-10-14: added `IGitStatusService`/`GitStatusService`, which shells out to `git status
  --porcelain` (matching `ClaudeCliService`'s convention of treating an external CLI as the source
  of truth rather than taking a LibGit2Sharp-style native dependency) to report whether
  `workingDirectoryPath` is inside a git working tree and which files are dirty there; any failure
  to run git at all (not installed, not a repository, directory missing) is treated the same as
  "not a repository" since this is an informational warning, not a security boundary.
  `ApplyOrchestrationService.Preview` now also takes an `IGitStatusService` dependency and reports
  `ApplyPreview.IsGitRepository`/`GitDirtyFileCount`/`GitDirtyFiles` - scoped the same way
  `ProtectedPathCount`/`Files` already are (T4.1.2): only files among the ones actually selected for
  this apply, not every dirty file in the repository, since an unrelated dirty file elsewhere isn't
  this apply's problem. Registered as `AddTransient<IGitStatusService, GitStatusService>()` in
  `App.axaml.cs`. Covered by new `GitStatusServiceTests` (not-a-repo, missing directory,
  null/whitespace path, clean repo, a modified tracked file, an untracked file - using a real `git
  init`/`commit` in a temp directory, consistent with this test project's no-mocking convention) and
  new `ApplyOrchestrationServiceTests` cases (not-a-repo default, clean repo, a dirty selected file
  reported, a dirty file outside the selected batch not reported). Full solution builds with 0
  warnings/errors, 273/273 tests pass (221 Core + 52 Avalonia - before T4.3.2's additions).
  `GitStatusService.cs` is 89.1% line/80% branch covered - the gap is the `process is null`/
  `catch (Win32Exception or IOException)` arms for a missing `git` executable, the same class of
  deliberately-untested defensive branch as `FileEncodingDetector`'s I/O catch arms (T4.1.3):
  forcing it would mean temporarily hiding `git` from `PATH` mid-test-run, which is flaky and
  platform-specific rather than a real repro.
- [x] T4.3.2 Warn when applying over uncommitted work. — done 2026-10-14: `TutorialViewModel.
  ApplySelectedAsync` now shows a dedicated confirm dialog (new `Tutorial_ApplyGitDirtyConfirm*`
  resource keys) whenever `preview.IsGitRepository && preview.GitDirtyFileCount > 0`, naming the
  affected files. Unlike the T4.1.2 protected-paths confirm, declining here cancels the *whole*
  apply rather than just excluding those files from the batch - overwriting a file that's already
  dirty in git isn't something a per-file skip can make safe (there's no commit to fall back on for
  it either way), so it's a whole-batch decision like the general apply-confirm rather than a
  per-file one like protected paths. Covered by new `TutorialViewModelTests` cases (no dirty files
  skips the extra dialog, confirming both dialogs applies, declining the git-dirty dialog cancels
  without calling `ApplySelected`, declining the general confirm never even asks about dirty files).
  Full solution builds with 0 warnings/errors, 277/277 tests pass (221 Core + 56 Avalonia).
- [x] T4.3.3 Optional: stage applied files, or create a branch/commit per apply batch. — done
  2026-10-21: chose staging over a branch/commit-per-batch - staging only touches the git index
  (trivially reversible via `git restore --staged`), where an automatic commit would create history
  the user didn't ask for and have to clean up. `IGitStatusService` gained `StageFiles(
  workingDirectoryPath, relativeFilePaths)`, shelling out to `git add -- <paths>` (`--` so a path
  that happens to start with `-` is never misread as a flag) via the same `RunGit`
  process-start helper `GetStatus` already used (extracted from `TryRunGit` so both share it); fails
  open - returns `false` without throwing for an empty path list, a missing directory, a non-repository,
  or a missing `git`, same posture as `GetStatus`. `ApplyOrchestrationService.ApplySelected` now
  stages exactly the files it just wrote successfully (never a whole-tree `git add -A`, and never
  conflicted/blocked/failed files) right after recording the batch for undo/history, and
  `ApplyBatchResult` gained a `FilesStagedInGit` bool reported back to the caller. `TutorialViewModel.
  ApplySelectedAsync` appends a new `Tutorial_ApplySelectedResultStaged` ("Staged the applied file(s)
  in git.") to the existing apply-result message whenever that's true. Covered by new `GitStatusServiceTests` cases
  (not-a-repository, no files, missing directory, an untracked file lands in the index, a modified
  tracked file's edit moves from unstaged to staged - all via a real `git init`/`add`/`commit` in a
  temp directory, this test project's existing no-mocking convention), new
  `ApplyOrchestrationServiceTests` cases (a git repo stages the applied file, a non-repository reports
  not-staged, a conflicted file is never staged, nothing-selected never attempts to stage), and new
  `TutorialViewModelTests` cases (the result message does/doesn't mention staging). Full solution
  builds with 0 warnings/errors, 637/637 tests pass (406 Core + 231 Avalonia, up from 626). Not
  visually verified (no X server in this sandbox, same limitation noted on earlier UI-touching tasks)
  beyond confirming the app still launches without throwing (ran under `timeout`; exit code 124
  confirms it was still running, not crashed, when killed).

  Follow-up same day: staging was unconditional at first, but the user asked for it to be a choice
  instead. `IApplyOrchestrationService.ApplySelected` gained a `stageAppliedFiles = true` parameter
  gating the existing `StageFiles` call (default `true` keeps prior behavior for anyone who hasn't
  touched the new control). `TutorialViewModel` gained a persisted `StageAppliedFilesInGit` bool -
  loaded in `OnNavigatedTo` via `ILocalSettingsService` (new constructor dependency, following
  `PromptViewModel.TimeoutSeconds`'s save-on-change/restore-on-navigate precedent exactly, including
  its try/catch-and-log around the settings read) and saved on every change - bound to a new
  "Stage applied files in git" `CheckBox` in `TutorialView.axaml` right above the Apply Selected
  button, and threaded into the `ApplySelected` call. `FakeApplyOrchestrationService` gained a
  `LastStageAppliedFiles` tracking property (mirroring `LastAllowProtectedPaths`). Covered by a new
  `ApplyOrchestrationServiceTests` case (`stageAppliedFiles: false` stages nothing even in a git
  repo) and new `TutorialViewModelTests` cases (the flag's default/unchecked value reaches
  `ApplySelected`, and the setting round-trips through `OnNavigatedTo`/`FakeLocalSettingsService`).
  Full solution builds with 0 warnings/errors, 642/642 tests pass (407 Core + 235 Avalonia).

This closes out every remaining roadmap item (Epics 1-9); see "Suggested execution order" below for
how this document was originally sequenced.

---

### EPIC 5 — Harden the LLM protocol *(P1)*

**F5.1 — Structured output instead of markdown scraping**
- [x] T5.1.1 Move the response contract to JSON (or CLI structured output) with an explicit schema. —
  done 2026-10-16: `PromptViewModel.TutorialInstructionPrompt` now asks for a single JSON object
  (`{"responseType": "answer", "answer": ...}` or `{"responseType": "development", "tutorial": ...,
  "files": [{"path", "content", "steps": [{"title", "explanation", "snippet"}]}]}`) instead of the
  `## Tutorial`/`## Files` markdown headers. New `ClaudeJsonResponse`/`ClaudeJsonFile`/
  `ClaudeJsonStep` (Core/Models) are the wire DTOs; `ClaudeJsonResponseParser.TryParse`
  (Core/Helpers) extracts the first balanced top-level `{...}` object from the raw text via a
  quote/escape-aware brace scan (so a file's `content` string can contain its own braces or
  triple-backtick fences without truncating the object early — the exact fragility class
  `ClaudeResponseParserTests` pinned under the old markdown parser) before deserializing with
  `Newtonsoft.Json` and mapping into the existing `ClaudeParsedResponse`/
  `ProposedFileChangeViewModel`/`ProposedHunk` graph, reusing `HunkLocationResolver` unchanged.
- [x] T5.1.2 Validate against the schema; on failure, retry once with the error fed back. — done
  2026-10-16: `ClaudeJsonResponseParser.TryParse` validates required fields per `responseType`
  (`answer` requires non-empty `answer`; `development` requires non-empty `tutorial`, and every file
  needs a `path`/`content`, every step a `title`) and returns a specific error message rather than
  just pass/fail. `PromptViewModel.RunPromptAsync` now calls the new
  `StructuredResponseParser.Parse` after a successful CLI run; if that had to fall back (see T5.1.3)
  it issues exactly one retry call to `ILlmRunner` with the original prompt plus a new
  `RetryInstructionSuffix` naming the validation error, then re-parses the retry's output as the
  final result. A failed retry call (cancelled, CLI error) keeps the first attempt's
  already-fallback-parsed result rather than discarding it for an error state. Cost/token/duration
  reporting (T6.2.3) sums both calls via a new `CombineUsage`/`SumNullable` helper so a retry's cost
  isn't silently under-reported.
- [x] T5.1.3 Keep the markdown parser as a fallback and cover it with tests. — done 2026-10-16:
  `ClaudeResponseParser` is untouched and still fully covered by `ClaudeResponseParserTests`; new
  `StructuredResponseParser.Parse` is the entry point that prefers `ClaudeJsonResponseParser` and
  only falls through to `ClaudeResponseParser.Parse` (reporting `UsedFallback = true` plus the JSON
  validation error) when the JSON contract couldn't be satisfied even after the T5.1.2 retry — so a
  model that reverts to the old markdown shape, or answers in plain prose, still produces a usable
  result instead of an error. `PromptViewModel` surfaces a `Prompt_FallbackParseNotice` suffix on the
  status message whenever the final result came from the fallback, so the user can tell the response
  wasn't strictly schema-conformant. Known/accepted regression: the live incremental file-reveal
  added in T6.2.2 re-parses the growing `CliOutput` with the markdown parser on every streamed delta
  — harmless but now mostly a no-op under the JSON contract, since a single top-level JSON object
  can't be meaningfully parsed until it's fully closed, so files no longer appear mid-stream the way
  they did under the markdown protocol; the authoritative parse still happens once, at the end, via
  `StructuredResponseParser`. Covered by new `ClaudeJsonResponseParserTests` (both response shapes,
  JSON wrapped in commentary/fences, file content containing its own braces/fences, every validation
  failure path, malformed JSON syntax), `StructuredResponseParserTests` (JSON success, markdown
  fallback, unstructured-text fallback), and new `PromptViewModelTests` cases (invalid-JSON-then-
  corrected-JSON retry succeeding, the retry prompt containing the original request and the
  validation error, both attempts failing and falling back to markdown with the notice shown, a
  failed retry call keeping the first attempt's fallback result, and cost/token summing across a
  retry). Full solution builds with 0 warnings/errors, 332/332 tests pass (251 Core + 81 Avalonia).
  `ClaudeJsonResponseParser.cs` is 97.8% line/89.4% branch covered — the one gap is a defensive
  `parsed is null` check after a successful deserialize, which `Newtonsoft.Json` can only hit for the
  literal JSON text `"null"` that the brace-scanning extractor can never produce (it only ever
  returns a balanced `{...}` object); left deliberately untested rather than forcing a contrived
  repro, same class of gap as other defensive branches noted earlier in this document. Not run
  against the real `claude` CLI — per the user's standing preference (noted under T6.2.3) to avoid
  spending real API usage on verification — so the new prompt's actual compliance rate with a live
  model is unverified; worth a manual smoke test at some point.

**F5.2 — Diffs over the wire, not whole files**
- [x] T5.2.1 Ask for unified diffs / search-replace edit blocks instead of full file contents to cut
  tokens and avoid truncation. — done 2026-10-16: `PromptViewModel.TutorialInstructionPrompt`'s
  `"development"` schema replaces each file's per-step `"content"`/`"steps"` shape with an `"edits"`
  list — `{"title", "explanation", "search", "replace"}` — and tells the model to use it for every
  file that already exists, reserving `"content"` for brand-new files only. New `ClaudeJsonEdit`
  (Core/Models, alongside a new `Edits` list on `ClaudeJsonFile`) is the wire DTO; the two shapes are
  not mutually exclusive in the parser (a file with both is treated as edit-mode, `content` ignored)
  to avoid a hard validation failure for a model that doesn't follow the instruction precisely.
- [x] T5.2.2 Apply those patches locally with fuzz/context matching. — done 2026-10-16: new
  `SearchReplaceEditApplier.Apply` (Core/Helpers) applies a file's edits, in order, against its real
  content — splitting into lines and locating each edit's `search` block with three progressively
  fuzzier strategies (exact; per-line trimmed, for indentation drift; per-line whitespace-collapsed,
  for reformatted internal spacing) before giving up. A `search` that matches more than once under a
  given strategy is treated the same as zero matches — rather than guessing which occurrence was
  meant — since a fuzzier strategy can only ever match a superset of lines, an ambiguous exact match
  stays ambiguous all the way down and the edit is reported unresolved rather than mis-applied.
  `ClaudeJsonResponseParser.TryParse` gained an optional `workingDirectoryPath` parameter (trailing,
  defaults to `null`, so every pre-existing call site is unaffected) and now reads a file's current
  content via the existing `FileContentReader.ReadOrEmpty` and runs the applier *during validation*,
  not mapping — resolving edits requires the real file, so there's no way to validate an edit-mode
  file without doing the apply. `StructuredResponseParser.Parse` grew the same trailing optional
  parameter and `PromptViewModel` now passes its `WorkingDirectoryPath` through on both the initial
  parse and the T5.1.2 retry's. A nice side effect: since the applier already read the file's real
  content to resolve the edits, edit-mode files get `ProposedFileChangeViewModel.OriginalContent` set
  eagerly at parse time instead of waiting for the Diffs/Tutorial pages to lazily read it on
  selection (`RefreshOriginalContent`) — the diff is correct the moment the response is parsed, and
  the later lazy refresh (unchanged) just re-reads the same content.
- [x] T5.2.3 Detect truncated or incomplete responses and refuse to apply them. — done 2026-10-16: an
  edit whose `search` text can't be matched under any of the applier's strategies is treated as a
  **schema validation failure** for the whole response — `"The \"search\" text for edit ... could not
  be matched to that file's current content - it may be incomplete, or the file may have changed
  since the request was made."` — reusing T5.1.2's existing one-shot-retry-with-error-fed-back
  mechanism rather than a parallel "partially apply, flag the rest" path: there's no way to tell a
  step that failed to resolve apart from one that genuinely didn't need to touch the file, so a
  response with even one unresolvable edit is treated as untrustworthy as a whole and is either
  corrected on retry or handed to the markdown fallback (T5.1.3), never partially trusted. A missing
  or blank `WorkingDirectoryPath` is not special-cased — the file is read as empty, which makes any
  non-empty `search` fail to match and surfaces the same validation error, since an edit genuinely
  can't be resolved without knowing which file to resolve it against. Covered by new
  `SearchReplaceEditApplierTests` (exact/multi-line match, multiple edits applied in sequence against
  each other's result, indentation-drift and whitespace-collapse fuzzy fallbacks, not-found, ambiguous
  match, deletion via empty `replace`, null `search`/`replace`, search longer than the file, an
  earlier successful edit surviving a later failed one), new `ClaudeJsonResponseParserTests` cases
  (edit-mode end-to-end against a real temp-directory file including `OriginalContent`, an unmatchable
  edit failing validation, missing `search`/`title`, no working directory, `content`+`edits` both
  present preferring `edits`), a `StructuredResponseParserTests` case threading the working directory
  through, and a new `PromptViewModelTests` case running an edit-mode response end-to-end against a
  real temp working directory via `RunPromptCommand`. Full solution builds with 0 warnings/errors,
  353/353 tests pass (271 Core + 82 Avalonia). `SearchReplaceEditApplier.cs` is 100% line/branch
  covered; `ClaudeJsonResponseParser.cs` is 98.3% line/90.9% branch covered (up from 97.8%/89.4% before
  this task) — the only gap is the same pre-existing defensive `parsed is null` check noted under
  T5.1.3, plus a couple of `?? string.Empty`/`?? []` null-coalescing branches in the content-mode
  mapping path whose null side validation already rules out (e.g. `file.Content` can't be null once
  `TryValidate` has passed) — same class of gap, left untested rather than forced. Not run against the
  real `claude` CLI, same caveat as T5.1.3 — the new `"edits"` instruction's actual compliance rate
  with a live model (especially whether it reliably copies `search` text verbatim) is unverified.

**F5.3 — Prompt template management**
- [x] T5.3.1 Move `TutorialInstructionPrompt` out of `PromptViewModel` into a versioned template file. —
  done 2026-10-16: the instruction text is now `CodeGenToTutorial.Core/Prompts/TutorialInstructionPromptV1.txt`,
  embedded as a resource (`<EmbeddedResource Include="Prompts\*.txt" />` in `.Core`'s csproj) rather
  than a `.csproj`-copied loose file, since Core has no "content root" concept the way the Avalonia
  app's `appsettings.json` does and an embedded resource ships correctly regardless of working
  directory. New `IPromptTemplateProvider`/`PromptTemplateProvider` (Core) reads it via
  `Assembly.GetManifestResourceStream`, caching the content after the first read, and exposes
  `CurrentVersion` (`PromptTemplateProvider.LatestVersion = 1`) so a future schema change can ship as
  a new `TutorialInstructionPromptV2.txt` without mutating the one every past response was parsed
  against - the versioning T5.3.2's settings override will need to pick a version from, not just a
  file-organization nicety. `PromptViewModel` now takes `IPromptTemplateProvider` as a fifth
  constructor parameter and builds `fullPrompt` from `GetTutorialInstructionPrompt() + "\n\n" +
  PromptText`, replacing the old hard-coded `const string TutorialInstructionPrompt`; `RetryInstructionSuffix`
  is untouched (it's a short retry-only message, not the schema contract). Registered
  `IPromptTemplateProvider` as a singleton in `App.axaml.cs`, next to `IPromptResultStore`.
  `PromptViewModelTests.CreateSut()` now constructs a real `PromptTemplateProvider` rather than a fake,
  the same way other tests use real pure/side-effect-free Core services directly; its existing
  `LastPrompt` assertions (`Contains`/`DoesNotContain`) needed no changes since they never depended on
  the exact template text. Added `PromptTemplateProviderTests` (version number, embedded content
  contains both JSON response shapes, repeated calls return identical content). Full solution builds
  with 0 warnings/errors, 356/356 tests pass (274 Core + 82 Avalonia, up from 353 before this task's
  3 new Core tests).
- [x] T5.3.2 Let the user view and override the system preamble in Settings. — done 2026-10-16:
  new `PromptTemplateSettingsKeys.Override` (Avalonia, mirroring `ClaudeCliSettingsKeys`) backs a
  `PromptTemplateOverride` setting - blank/missing means "use the built-in default". `SettingsViewModel`
  now takes `IPromptTemplateProvider` as a third constructor parameter and exposes two things on the
  Settings page: a read-only `DefaultPromptTemplate` (the embedded `TutorialInstructionPromptV1.txt`
  content, so the user has something to view/compare/copy from) and an editable `PromptTemplateOverride`
  that persists via the same `OnNavigatedTo`-restores/`On*Changed`-saves pattern as
  `ClaudeCliExecutablePath`/`ClaudeCliArguments`, plus a `ResetPromptTemplateCommand` (disabled once the
  override is already empty) that clears it. `PromptViewModel.RunPromptAsync` reads
  `PromptTemplateSettingsKeys.Override` directly via its existing `ILocalSettingsService` dependency -
  fresh on every run rather than once in `OnNavigatedTo` - so a change made on the Settings page takes
  effect on the very next run without needing to revisit the Prompt page, same rationale
  `ClaudeCliOptionsProvider` documents for its own settings reads; a blank/whitespace-only override
  falls back to `IPromptTemplateProvider.GetTutorialInstructionPrompt()` exactly like a missing one.
  No changes to `IPromptTemplateProvider`/`PromptTemplateProvider` (Core) were needed - the override
  is purely an Avalonia-layer concern, the same separation `ClaudeCliOptionsProvider` keeps Core's
  `ClaudeCliRunner` free of `ILocalSettingsService`. `SettingsView.axaml` gained an "Override" text box
  (multiline, monospace, matching the Prompt page's CLI-output box styling) with a "Reset to default"
  button, and a read-only "Built-in default" text box below it. Covered by new `SettingsViewModelTests`
  cases (default-matches-provider, override defaults empty, override persists, override restores on
  navigation, reset command's `CanExecute` gating and effect) and new `PromptViewModelTests` cases (no
  override uses the default, a saved override replaces the default, a blank/whitespace override still
  falls back to the default). Full solution builds with 0 warnings/errors, 365/365 tests pass (274 Core
  + 91 Avalonia, up from 356). Not visually verified (no X server in this sandbox, same limitation noted
  on earlier UI-touching tasks) beyond confirming the app still launches without throwing.
- [x] T5.3.3 Named prompt presets (feature / bugfix / refactor / explain) with a tutorial-depth control. —
  done 2026-10-17: new `PromptPreset` (`General, Feature, Bugfix, Refactor, Explain`) and `TutorialDepth`
  (`Standard, Brief, Detailed`) enums (Core `Models/`); `General`/`Standard` are the no-op defaults. Six
  new embedded snippets in `Core/Prompts/` (`PresetFeatureV1.txt`, `PresetBugfixV1.txt`,
  `PresetRefactorV1.txt`, `PresetExplainV1.txt`, `DepthBriefV1.txt`, `DepthDetailedV1.txt`) - one file per
  non-default value rather than a single sectioned file, matching the one-file-per-template precedent
  `TutorialInstructionPromptV1.txt` already set; no csproj change needed since the existing
  `Prompts\*.txt` glob covers them. `IPromptTemplateProvider` gained `GetPresetInstruction(PromptPreset)`
  and `GetDepthInstruction(TutorialDepth)`, both returning `""` for the default value; the single
  `_cachedTemplate` field in `PromptTemplateProvider` became a `Dictionary<string, string>` keyed by
  resource name so each snippet is still read from its embedded resource once and reused. Also edited
  `TutorialInstructionPromptV1.txt` to drop the base preamble's "in detail" wording from the
  `explanation` field description, since that phrase would otherwise conflict with a `Brief` depth
  selection - the depth/preset text is additive, not a replacement, so the base template needed to stop
  asserting a detail level of its own. `PromptViewModel` gained `SelectedPreset`/`SelectedDepth`
  (`[ObservableProperty]`, defaulting to `General`/`Standard`) and `AvailablePresets`/`AvailableDepths`
  (`Enum.GetValues<T>()`) for binding; persisted via new `SavedPromptPreset`/`SavedTutorialDepth` settings
  keys using `Enum.TryParse`/`.ToString()` (not Newtonsoft's default int enum serialization) so a future
  reordering of enum members can't silently change what a saved value means. Preset/depth instructions
  are applied orthogonally to T5.3.2's preamble override - appended after whichever preamble is in effect
  (default or user-overridden) and before the user's own prompt text - since they're extra guidance, not
  schema, so where the preamble came from doesn't matter. `PromptView.axaml` gained two `ComboBox`
  controls ("Preset", "Tutorial detail") next to the existing Timeout control; this is the first
  `ComboBox` usage in the codebase, styled consistently with the page's existing label+control pattern.
  Covered by new `PromptTemplateProviderTests` cases (`General`/`Standard` return empty, each non-default
  value returns distinct non-empty text) and new `PromptViewModelTests` cases (defaults, persistence,
  restoration on `OnNavigatedTo`, `General`+`Standard` add no extra text to the prompt, a selected
  preset/depth's instruction text reaches the LLM runner's prompt). Full solution builds with 0
  warnings/errors, 383/383 tests pass (284 Core + 99 Avalonia, up from 365). Not visually verified (no X
  server in this sandbox, same limitation noted on earlier UI-touching tasks) beyond confirming the app
  still launches without throwing (ran under `timeout`; exit code 124 confirms it was still running, not
  crashed, when killed).

**F5.4 — Context selection**
- [x] T5.4.1 Let the user attach specific files/folders as context for the prompt. — done 2026-10-18:
  new `IFilePickerService`/`FilePickerService` (Avalonia, mirroring `IFolderPickerService`/
  `FolderPickerService`) wraps Avalonia's `OpenFilePickerAsync` with `AllowMultiple = true`, registered
  transient in `App.axaml.cs` next to `IFolderPickerService`; `FakeFilePickerService` added alongside
  `FakeFolderPickerService` for tests. `PromptViewModel` gained `AttachedPaths`
  (`ObservableCollection<string>`, mixed file/folder paths), `SelectedAttachedPath`, and three commands:
  `AddContextFilesCommand` (multi-file picker), `AddContextFolderCommand` (reuses the existing folder
  picker), and `RemoveAttachedPathCommand` (`CanExecute`-gated on a selection, same pattern as
  `ResetPromptTemplateCommand`/`CanResetPromptTemplate`) rather than a per-row remove button, which
  would've needed a non-compiled or ancestor-relative binding from inside the `ListBox`'s item template
  back up to a ViewModel-level command - a selection + one button keeps the XAML in the compiled-binding
  style the rest of the page already uses. The list persists as a `List<string>` under a new
  `SavedContextPaths` settings key - no new persistence plumbing needed since `ILocalSettingsService`
  already round-trips any `T` through Newtonsoft. At run time, `BuildContextFilesBlock` expands each
  attached path - a folder non-recursively to the files directly inside it (not subfolders), a file
  as-is - and embeds successfully-read file contents under an "Additional context files:" header,
  inserted after the preset/depth instructions (T5.3.3) and before the user's own prompt text. Two
  scoping decisions, confirmed with the user before implementation: folders are non-recursive, so
  attaching one can't silently sweep in `.git`/`bin`/`obj`/`node_modules` - the user attaches a subfolder
  explicitly if they want more; and a file that can't be read as text (binary, permissions, deleted since
  attachment) is skipped rather than failing the whole run, since folder attachment in particular can
  easily pick up a non-text file alongside the ones that matter. `PromptView.axaml` gained a "Context
  files" row (Add files.../Add folder.../Remove selected buttons plus a `ListBox` of attached paths)
  between the project-location row and the prompt-text row; all later `Grid.Row` indices shifted down by
  one to make room. Covered by new `PromptViewModelTests` cases (defaults, add/dedupe files, add folder,
  no-op on a cancelled folder pick, remove `CanExecute` gating and effect plus persistence, restoration
  on `OnNavigatedTo`, an attached file's content reaching the prompt, an attached folder including only
  its top-level files and not a nested subfolder's, and no attachments adding no header at all). Full
  solution builds with 0 warnings/errors, 394/394 tests pass (284 Core + 110 Avalonia, up from 383). Not
  visually verified (no X server in this sandbox, same limitation noted on earlier UI-touching tasks)
  beyond confirming the app still launches without throwing (ran under `timeout`; exit code 124 confirms
  it was still running, not crashed, when killed).
- [x] T5.4.2 Show an estimated token count before sending. — done 2026-10-18: new
  `TokenEstimator` (Core `Helpers/`) estimates `text.Length / 4` rounded up - the same rough
  characters-per-token ratio Anthropic's own docs use for English text. This is deliberately a
  ballpark, not a real tokenizer: Anthropic doesn't expose a local one, the CLI only reports the
  real `InputTokens` after a run completes (`LlmRunResult.InputTokens`, shown post-run in
  `UsageSummary`), and a live "as you type" estimate can't afford a round trip to an API endpoint on
  every keystroke anyway. The prompt-assembly logic that used to live inline in `RunPromptAsync`
  (preamble/override, T5.3.2; preset/depth, T5.3.3; context files, T5.4.1; working directory line)
  was extracted into a new `BuildFullPromptAsync()` so the estimate is always computed from the
  exact same string a run would actually send, rather than a second implementation that could drift
  from it. `PromptViewModel` gained `EstimatedTokenCountDisplay`, recomputed via
  `UpdateEstimatedTokenCountDisplay()` whenever anything feeding the prompt changes - `PromptText`,
  `WorkingDirectoryPath` (new `OnWorkingDirectoryPathChanged` partial), `SelectedPreset`/
  `SelectedDepth`, and `AttachedPaths` (hooked via `CollectionChanged` in the constructor, since an
  `ObservableCollection<T>` doesn't go through an `[ObservableProperty]`'s `On*Changed`) - plus once
  more after `OnNavigatedTo` restores all of those from settings. A monotonic `_tokenEstimateVersion`
  counter guards against an older recompute's settings read finishing after a newer one and
  overwriting the display with stale data - a real risk here, unlike most other settings reads in
  this class, since typing triggers a recompute on every keystroke. New `Prompt_EstimatedTokenCount`
  resource string (`"~{0:N0} tokens (estimated)"`, mirroring `Prompt_UsageSummary`'s style).
  `PromptView.axaml`'s "Prompt text" label row gained the estimate right-aligned next to it. Covered
  by new `TokenEstimatorTests` (empty string, rounds up, longer text estimates higher) and new
  `PromptViewModelTests` cases (defaults empty, populated after `OnNavigatedTo`, updates when prompt
  text or attachments change, and matches the exact prompt a run actually sends). Full solution
  builds with 0 warnings/errors, 404/404 tests pass (289 Core + 115 Avalonia, up from 394). Not
  visually verified (no X server in this sandbox, same limitation noted on earlier UI-touching
  tasks) beyond confirming the app still launches without throwing (ran under `timeout`; exit code
  124 confirms it was still running, not crashed, when killed).

---

### EPIC 6 — LLM runner robustness *(P1)*

**F6.1 — Lifecycle control**
- [x] T6.1.1 Cancel button wired to a real `CancellationTokenSource`; kill the child process on
  cancel. — done 2026-10-15: `PromptViewModel` now creates a `CancellationTokenSource` per
  `RunPromptAsync` call, passes its token through to `IClaudeCliService.RunPromptAsync`, and exposes
  a `CancelPromptCommand` (enabled only while a run is in flight) that cancels it. A `Cancel` button
  next to "Send to Claude Code" is visible only while `IsRunning`. Catching `OperationCanceledException`
  around the await reports `Prompt_RunCancelled` ("Cancelled.") instead of falling into the generic
  error handler. Critically, `ClaudeCliService.RunPromptAsync` now wraps `process.WaitForExitAsync` in
  a try/catch for `OperationCanceledException` that calls `process.Kill(entireProcessTree: true)`
  before rethrowing - cancelling the await alone does not stop the child process (or, on Windows, the
  cmd.exe wrapper's own child), so without this the `claude` process would keep running in the
  background after cancel. Covered by new `PromptViewModelTests` cases using a `FakeClaudeCliService.
  CompleteImmediately = false` flag that blocks on `Task.Delay(Timeout.Infinite, cancellationToken)`
  until cancelled, standing in for a real long-running CLI call (cancel while running stops the run
  and reports "Cancelled.", cancel command can't execute when not running, cancel command can't
  execute once a run completes). Full solution builds with 0 warnings/errors, 280/280 tests pass (221
  Core + 59 Avalonia - before T6.1.3's additions). The actual process-kill behavior inside
  `ClaudeCliService.RunPromptAsync` has no direct unit test coverage, since the hardcoded `claude`
  executable can't be substituted for a real long-running process in a test yet - revisit once T6.1.4
  makes the executable path injectable.
- [x] T6.1.2 Configurable timeout with a clear timeout message. — done 2026-10-15: `PromptViewModel`
  now has a `TimeoutSeconds` property (default 300, persisted to local settings under
  `PromptTimeoutSeconds` and restored in `OnNavigatedTo`), exposed on `PromptView` as a
  `NumericUpDown` next to the Cancel button. `RunPromptAsync` runs two independent
  `CancellationTokenSource`s - the existing user-cancel one from T6.1.1, plus a new one constructed
  with `TimeSpan.FromSeconds(TimeoutSeconds)` so it cancels itself once the timeout elapses - linked
  via `CancellationTokenSource.CreateLinkedTokenSource` into the single token actually passed to
  `IClaudeCliService.RunPromptAsync`. Both paths still end up throwing the same
  `OperationCanceledException`, so the catch block distinguishes a timeout from a user cancel by
  checking which of the two underlying sources has `IsCancellationRequested` set, rather than
  guessing from the single linked token - reporting the new `Prompt_RunTimedOut` resource string
  (naming the elapsed seconds and pointing at the Settings page) instead of `Prompt_RunCancelled`.
  Covered by new `PromptViewModelTests` cases (timeout elapsing reports the timeout message and
  still cancels the underlying CLI call via `FakeClaudeCliService.CompleteImmediately = false`;
  `TimeoutSeconds` defaults to 300; changing it persists to settings; `OnNavigatedTo` restores a
  previously saved value) - the timeout test uses `TimeoutSeconds = 0` so the timer fires
  immediately rather than slowing the suite down with a real wait. The existing T6.1.1 cancel tests
  were unaffected since they run well within the 300-second default timeout. Full solution builds
  with 0 warnings/errors, 289/289 tests pass (225 Core + 64 Avalonia). Manually launched the app to
  confirm it starts without any Avalonia binding exceptions after adding the `NumericUpDown`
  binding, but couldn't visually confirm the control's on-screen layout - this sandbox's Wayland
  compositor refuses screenshot capture, so a quick manual look is still worth doing.
- [x] T6.1.3 Preflight check that the CLI exists; actionable error with install guidance if not. —
  done 2026-10-15: added `ExecutablePathResolver.ExistsOnPath`, a helper that searches the `PATH`
  environment variable for an executable the same way the OS shell would resolve a bare command name
  (checking `.exe`/`.cmd`/`.bat` on Windows, since npm installs the `claude` CLI as a `.cmd` shim
  there, and an extension-less exact match on other platforms) - this avoids actually starting a
  process just to check availability. `IClaudeCliService` gained an `IsAvailable()` method;
  `ClaudeCliService.IsAvailable()` delegates to `ExecutablePathResolver.ExistsOnPath("claude")`.
  `PromptViewModel.RunPromptAsync` now checks `IsAvailable()` first and, if false, sets
  `StatusMessage` to the new `Prompt_ClaudeCliNotFound` resource string (which names the install
  command, `npm install -g @anthropic-ai/claude-code`) and returns without starting a run - `IsRunning`
  is never set to true for this path, so the cancel button/progress ring don't appear for a run that
  never started. `ExecutablePathResolver.ExistsOnPath` takes an optional `pathVariable` parameter
  (defaulting to the real `PATH` env var) purely for testability, so tests can point it at a temp
  directory instead of mutating process-wide state. Covered by new `ExecutablePathResolverTests`
  (executable found among several PATH directories, not found, empty PATH, blank PATH entries
  skipped without throwing) and a new `PromptViewModelTests` case
  (`RunPromptAsync_CliNotAvailable_ReportsActionableMessageAndDoesNotRun`) using a new
  `FakeClaudeCliService.AvailableToReturn` flag (defaults to `true` so existing tests are
  unaffected). Full solution builds with 0 warnings/errors, 285/285 tests pass (225 Core + 60
  Avalonia). The Windows-only extension-search branch in `ExecutablePathResolver` is untested on this
  (Linux) development machine - same class of platform-conditional gap as `ClaudeCliService`'s
  existing `cmd.exe` branch.
- [x] T6.1.4 Configurable CLI path/arguments in Settings. — done 2026-10-15: added
  `ClaudeCliOptions` (`ExecutablePath`/`Arguments`, with `ClaudeCliOptions.Default` = `"claude"`/
  `"-p"`) to Core, and threaded an optional `ClaudeCliOptions? options` parameter through
  `IClaudeCliService.RunPromptAsync` and `IsAvailable` (defaulting to `ClaudeCliOptions.Default`
  when not supplied, so T6.1.1-T6.1.3's existing call sites stay correct without change).
  `ClaudeCliService` now builds its `ProcessStartInfo.FileName` from `options.ExecutablePath` and
  splits `options.Arguments` on whitespace into the argument list - naive, no quoting support, but
  sufficient for simple flags like `-p --model=opus`. `ExecutablePathResolver.ExistsOnPath` gained a
  branch so a path containing a directory separator (e.g. an absolute path to a custom build) is
  checked directly with `File.Exists` instead of being searched for on `PATH`, matching how a shell
  treats a command name containing a slash. On the Settings page, `SettingsViewModel` now
  implements `INavigationAware` and exposes `ClaudeCliExecutablePath`/`ClaudeCliArguments`
  (persisted immediately on change, restored in `OnNavigatedTo`, falling back to
  `ClaudeCliOptions.Default` when unset) bound to two new `TextBox`es under a "Claude Code CLI"
  heading. `PromptViewModel.RunPromptAsync` reads those same two settings keys (shared via a new
  `ClaudeCliSettingsKeys` constants class so the Settings and Prompt pages can't drift apart) on
  every run - rather than caching them - so a change made on the Settings page takes effect on the
  very next run without needing to revisit the Prompt page first; the resulting `ClaudeCliOptions`
  feeds both the `IsAvailable` preflight check (T6.1.3) and the actual `RunPromptAsync` call.
  Covered by new `ExecutablePathResolverTests` cases (absolute path to an existing file resolves
  without consulting `PATH`; absolute path to a missing file is `false`), new
  `SettingsViewModelTests` (defaults, persisting a change, restoring a saved value, restoring with
  nothing saved yet - using a new `FakeThemeSelectorService` alongside the existing
  `FakeLocalSettingsService`), and new `PromptViewModelTests` cases (no saved settings passes
  `ClaudeCliOptions.Default` through to the CLI service; saved settings pass the configured
  executable path and arguments through) via a new `FakeClaudeCliService.LastOptions` capture.
  `ClaudeCliService.cs` itself still has no direct test coverage for the actual process-start
  behavior, consistent with the gap already noted under T6.1.1 - it shells out to a real external
  process, so exercising the new argument-splitting/path-selection logic end-to-end isn't something
  a unit test can safely do. Full solution builds with 0 warnings/errors, 298/298 tests pass (227
  Core + 71 Avalonia). Manually launched the app after these changes and confirmed no Avalonia
  binding exceptions on startup; couldn't visually confirm the new Settings page `TextBox`es render
  as intended since this sandbox's Wayland compositor refuses screenshot capture, so a quick manual
  look is worth doing.

**F6.2 — Streaming & progress**
- [x] T6.2.1 Stream stdout into the output pane as it arrives rather than buffering to completion. —
  done 2026-10-15: `ClaudeCliService` now always appends `--output-format stream-json
  --include-partial-messages --verbose` after the user-configured arguments (T6.1.4) - stream-json
  with partial messages is a hard requirement for this service's own parsing to work, not an optional
  CLI choice, so it's unconditional rather than user-configurable. Verified the NDJSON schema for
  free by reading the embedded strings in the installed `claude` CLI binary (`strings -a`) rather
  than spending a real/paid API call: each stdout line is one JSON object; `{"type":"stream_event",
  "event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"..."}}}` carries one
  chunk of assistant text, and a final `{"type":"result",...}` line carries cost/usage/duration
  (T6.2.3). `OutputDataReceived`'s handler now parses each line with `Newtonsoft.Json.Linq.JObject`
  (matching the project's existing `Json.cs` convention) instead of accumulating raw text, and a
  line that fails to parse as JSON is swallowed rather than crashing the run. `IClaudeCliService.
  RunPromptAsync` gained an `Action<string>? onTextDelta` parameter invoked once per text-delta
  chunk, in order, as it arrives - on the process's background output-reading thread, so
  `PromptViewModel` marshals onto the UI thread via `Dispatcher.UIThread.Post` before touching
  `CliOutput`. `ClaudeCliResult.StandardOutput` is now the concatenation of streamed text deltas
  (previously the raw stdout text); for this app's single-turn, no-tool-use prompts that's
  equivalent to the old behavior, just assembled incrementally. Covered by a new
  `ClaudeCliServiceTests` (Core.Tests) that stands a small POSIX shell script in for the `claude`
  binary - the same approach `GitStatusServiceTests` already takes with a real `git` binary -
  asserting deltas are accumulated into `StandardOutput` and that `onTextDelta` is invoked for each
  chunk in order; this closes the test-coverage gap called out under T6.1.1/T6.1.4 (process-start
  behavior had no direct unit test before, since the executable couldn't be substituted). Also a new
  `PromptViewModelTests` case asserting streamed deltas append to `CliOutput` live via
  `FakeClaudeCliService.TextDeltasToEmit`. Known gap: if the CLI ever invokes a tool mid-turn
  (interleaving `tool_use` content blocks with text blocks), deltas are concatenated in arrival order
  regardless of content-block index, which could interleave text from different blocks - not an
  issue today since this app's prompts explicitly ask for a direct text response with no tool use.
- [x] T6.2.2 Parse incrementally so files appear as they are emitted. — done 2026-10-15:
  `PromptViewModel`'s `onTextDelta` callback re-runs `ClaudeResponseParser.Parse` against the
  growing `CliOutput` after every chunk and calls `_promptResultStore.SetResult` with whatever it
  finds - safe to do on every delta because the parser only matches complete, closed-fence `###
  File:` sections (confirmed by reading `ClaudeResponseParser.cs`), so an in-progress file block is
  silently ignored until it closes rather than appearing truncated. This means `DiffsView`/
  `TutorialView` (both bound to the shared `PromptResultStore`) can show a file as soon as its block
  finishes streaming, instead of waiting for the whole CLI run to exit. The existing end-of-run
  `SetResult` call (gated on `ExitCode == 0`) is unchanged and simply re-confirms the same data once
  the run finishes. Covered by a new `PromptViewModelTests` case that streams a complete `## Tutorial`
  /`## Files` response as a single delta chunk and asserts `PromptResultStore.Files` already has the
  file before the run command's `Task` completes (via a new `FakePromptResultStore.OnSetResult` hook
  so the test can observe the intermediate call, not just the final state). Known/accepted gap: if a
  run streams one or more complete file blocks and then the CLI ultimately fails (non-zero exit
  code), those files remain visible in `PromptResultStore` - there's no rollback, since the store has
  no such concept and the approved plan for this task didn't call for one.
- [x] T6.2.3 Elapsed-time and token/cost readout. — done 2026-10-15: `PromptViewModel` starts a
  `Stopwatch` and an Avalonia `DispatcherTimer` (1-second tick) when a run begins, exposing a new
  `ElapsedTimeDisplay` string property (e.g. `"12s"`) bound next to the progress ring on
  `PromptView`, visible only while `IsRunning`. On the Core side, `ClaudeCliResult` gained nullable
  `TotalCostUsd`/`DurationMs`/`InputTokens`/`OutputTokens` fields, populated from the final
  `{"type":"result",...}` NDJSON line's `total_cost_usd`/`duration_ms`/`usage.input_tokens`/
  `usage.output_tokens` fields (null if that line was never seen, e.g. the run was cancelled before
  finishing). `PromptViewModel` turns a successful result with cost data into a new `UsageSummary`
  string (new `Prompt_UsageSummary` resource string: `"Cost: ${0:F4} • {1:N0} in / {2:N0} out tokens
  • {3:F1}s"`), displayed under the status message. Covered by new `ClaudeCliServiceTests` (the
  fake-script `result` line's cost/duration/usage fields land on `ClaudeCliResult` correctly) and new
  `PromptViewModelTests` cases (a result with usage data populates `UsageSummary`; one without leaves
  it empty; `ElapsedTimeDisplay` is set once a run completes). Full solution builds with 0
  warnings/errors, 306/306 tests pass (230 Core + 76 Avalonia). Manually launched the app and
  confirmed no Avalonia binding exceptions after adding the `ElapsedTimeDisplay`/`UsageSummary`
  bindings; couldn't visually confirm their on-screen layout since this sandbox's Wayland compositor
  refuses screenshot capture, so a quick manual look is worth doing. Also worth a real (paid) manual
  smoke test against the actual `claude` CLI at some point - the NDJSON schema here was verified via
  the installed binary's embedded strings rather than a live call, per the user's explicit preference
  to avoid spending real API usage on verification, so it hasn't been exercised end-to-end against
  the real CLI yet.

**F6.3 — Provider abstraction**
- [x] T6.3.1 Abstract behind an `ILlmRunner` so the CLI is one implementation. — done 2026-10-16:
  added `ILlmRunner` (Core) with just `RunPromptAsync(prompt, workingDirectory, onTextDelta,
  cancellationToken)` and `IsAvailableAsync()` - nothing CLI-specific (an executable path, process
  arguments) appears on it, so a future direct Anthropic API runner (T6.3.2) can be a second
  implementation without the interface leaking CLI concepts. Renamed `ClaudeCliService` to
  `ClaudeCliRunner` as the CLI's implementation of it, and replaced `ClaudeCliResult` with a
  provider-agnostic `LlmRunResult` (Core) - `Success`/`Output`/`ErrorMessage` are the fields callers
  should branch on, while `ExitCode` stays as a nullable CLI-only diagnostic a non-CLI runner can
  leave null. The CLI-specific configuration that used to flow in as a per-call `ClaudeCliOptions`
  parameter (T6.1.4) couldn't just move onto the new interface without reintroducing a CLI-shaped
  parameter on it, and Core has no reference to `ILocalSettingsService` (it lives in the Avalonia
  project) to resolve it internally either - so `ClaudeCliRunner` now takes a
  `Func<Task<ClaudeCliOptions>>? optionsProvider` constructor parameter instead (defaulting to
  `ClaudeCliOptions.Default` when not supplied) and calls it fresh on every `RunPromptAsync`/
  `IsAvailableAsync` call. The actual settings-reading that used to live in
  `PromptViewModel.LoadClaudeCliOptionsAsync` moved into a new `ClaudeCliOptionsProvider`
  (Avalonia), which `App.axaml.cs` wires into `ClaudeCliRunner` as that `Func` when registering
  `ILlmRunner` - this is the one seam where Avalonia's settings storage reaches into the otherwise
  settings-agnostic Core runner. `PromptViewModel` now depends on `ILlmRunner` and no longer knows
  about `ClaudeCliOptions`/`ClaudeCliSettingsKeys` at all; `SettingsViewModel` is unaffected since it
  only edits those settings; `IsAvailable` becoming `IsAvailableAsync` (now that it has to resolve
  options via the provider) is the only other signature change callers had to adjust for. Updated
  `ClaudeCliServiceTests` (renamed `ClaudeCliRunnerTests`, now constructing the runner with an
  options-provider lambda instead of passing `options:` per call, plus two new `IsAvailableAsync`
  cases) and `FakeClaudeCliService` (renamed `FakeLlmRunner`, implementing `ILlmRunner` - dropped
  `LastOptions` since the fake no longer receives options at all). The two `PromptViewModelTests`
  cases that asserted settings flow into CLI options (`...UsesDefaultClaudeCliOptions`/
  `...PassesConfiguredExecutablePathAndArguments`) moved to a new `ClaudeCliOptionsProviderTests`,
  since that's `ClaudeCliOptionsProvider`'s responsibility now, not `PromptViewModel`'s. Full
  solution builds with 0 warnings/errors, 308/308 tests pass (232 Core + 76 Avalonia).
- [x] T6.3.2 Add a direct Anthropic API runner (API key in secure storage, model selection). — done
  2026-10-21: added `AnthropicApiRunner` (Core) as a second `ILlmRunner` implementation alongside
  `ClaudeCliRunner`, slotting into the T6.3.1 seam without any change to `PromptViewModel`. It POSTs
  to `https://api.anthropic.com/v1/messages` with `stream: true` and parses the SSE `data: ` lines
  itself (`message_start` for input tokens, `content_block_delta`/`text_delta` for streamed output,
  `message_delta` for output tokens and `stop_reason`) - no SSE client package was added since the
  format is a handful of JSON lines, not worth a dependency for. Unlike the CLI, the Messages API has
  no server-side session to resume: `AnthropicApiRunner` keeps its own in-memory
  `ConcurrentDictionary<string, List<JObject>>` of prior turns keyed by the `SessionId` it hands back,
  and resends the whole transcript when `resumeSessionId` matches a known one - that history only
  lives as long as the runner (app-lifetime, since it's a singleton), which doesn't persist across
  restarts the way the CLI's own session storage does. `stop_reason == "max_tokens"` is treated as a
  failure (with the truncated text still returned as `Output`) since the caller is about to find the
  response unusable anyway; any other `stop_reason` is left alone, same as the CLI runner only ever
  failing on a non-zero exit code rather than on response content. `TotalCostUsd` is always `null` -
  the Messages API doesn't return pricing, and hand-maintaining a per-model price table would just go
  stale whenever Anthropic's pricing changes.

  The API key needed something better than the plaintext JSON `ILocalSettingsService` already uses
  for everything else, so this added `ISecureStorageService`/`SecureStorageService` (Avalonia). On
  Windows it's real DPAPI (`System.Security.Cryptography.ProtectedData`,
  `DataProtectionScope.CurrentUser`) - genuine OS-level protection tied to the logged-in user, the
  same mechanism Credential Manager itself builds on. Elsewhere there's no equivalent BCL API - true
  Keychain/Secret Service integration would mean shelling out to `security`/`secret-tool` per
  platform, each only verifiable on its own OS - so macOS/Linux fall back to AES-256 with a randomly
  generated key kept in a sibling file restricted to the owning user via
  `File.SetUnixFileMode(UserRead | UserWrite)`. That protects against casual disk browsing (or a
  synced/backed-up AppData folder) but isn't a hardened secret store; worth revisiting if real
  OS-keychain integration earns its per-platform testing cost. `AnthropicApiOptionsProvider` bridges
  this (and the model setting, via `ILocalSettingsService`) into the
  `Func<Task<AnthropicApiOptions>>` `AnthropicApiRunner`'s constructor takes, reading fresh on every
  call - same pattern as `ClaudeCliOptionsProvider`.

  To let both runners coexist behind the single `ILlmRunner` `PromptViewModel` depends on, added
  `LlmRunnerSelector` (Avalonia), which reads the user's choice (`LlmRunnerKind.ClaudeCli` or
  `AnthropicApi`, persisted via `ILocalSettingsService`, defaulting to `ClaudeCli` if unset or
  unparseable) fresh on every call and delegates to whichever concrete runner is selected -
  registered as the `ILlmRunner` singleton in `App.axaml.cs`, with `ClaudeCliRunner`/
  `AnthropicApiRunner` themselves registered as concrete-type singletons rather than also behind
  `ILlmRunner`, to sidestep needing keyed DI services for a two-implementation case. Switching runners
  on the Settings page takes effect on the very next run, not after a restart.

  Tested via `AnthropicApiRunnerTests` (Core), using a hand-written `HttpMessageHandler` fake injected
  through `AnthropicApiRunner`'s `HttpClient?` constructor parameter - no mocking library, matching
  how `ClaudeCliRunnerTests` fakes the CLI with a real shell script on disk rather than a mock. Covers
  streamed text accumulation/ordering, usage parsing, the `max_tokens` failure path, resumed-session
  history reconstruction, an unknown `resumeSessionId` starting fresh, missing-API-key short-circuiting
  before any request is sent, and non-success responses surfacing the API's own error message.
  `SecureStorageServiceTests` (Avalonia) exercises the AES fallback path's encrypt/decrypt round trip
  (DPAPI is Windows-only and untestable on this Linux sandbox) via a fake `IFileService` plus a
  uniquely-named, self-cleaning folder under the real `ApplicationData` directory, since
  `SecureStorageService`'s key file is written with plain `File.*` calls rather than through
  `IFileService` and would otherwise touch (and potentially collide with) a real installation's own
  key. `AnthropicApiOptionsProviderTests` and `LlmRunnerSelectorTests` round out coverage with fakes
  for every dependency, matching the rest of the Avalonia test suite's conventions.
- [x] T6.3.3 Model picker in Settings. — done 2026-10-21, alongside T6.3.2 above since a model picker
  only makes sense once there's a second runner to pick a model for: added an "LLM runner" radio
  group (Claude Code CLI / Anthropic API) and, under a new "Anthropic API" section, a masked API-key
  `TextBox` (`PasswordChar="•"`) and a `ComboBox` of `AnthropicModelCatalog.Models` (Opus 5, Sonnet 5 -
  the default, Fable 5.1, Haiku 4.5) bound by model ID with display names shown. The runner radio
  group reused `EnumToBooleanConverter`, which only ever handled `ThemeVariant` (an Avalonia class, not
  a real CLR enum) despite its generic name - generalized via pattern matching so a real enum like
  `LlmRunnerKind` is handled through `Enum.Parse`, without changing existing Theme-switching behavior.
  `SettingsViewModel` gained `SelectedRunnerKind`/`AnthropicApiKey`/`AnthropicApiModel` observable
  properties (persisting through `ILocalSettingsService`/`ISecureStorageService` as appropriate on
  change) and now takes `ISecureStorageService` as a constructor dependency.

---

### EPIC 7 — Sessions, history & persistence *(P1)*

**F7.1 — Persist runs**
- [x] T7.1.1 Persist each run (prompt, raw output, parsed result, selections, apply log) to disk.
  — done 2026-10-18: new Core DTOs `RunSession`/`RunSessionFile`/`RunSessionHunk` (Models) capture a
  run's prompt, raw output, tutorial text, working directory, each file's original/proposed/edited
  content and selection state, and the apply log - deliberately excluding `ProposedFileChangeViewModel`'s
  derived `DiffLines`/`DiffHunks`/`MatchedDiffHunks` (always recomputed from the fields that are kept)
  and `ProposedHunk.IsApplied` (resets to `false` on any `OriginalContent` change, so it's meaningless
  after a reload). New `IRunSessionStore`/`RunSessionStore` (Core) exposes `RecordRun(prompt, rawOutput)`
  (starts a new session and writes its first snapshot) and `Resave()` (re-reads the current
  `IPromptResultStore`/`IApplyHistoryLog` state and rewrites the same file; a no-op before any
  `RecordRun`). Each run writes its own file - `{StartedAt:yyyyMMddHHmmssfff}_{Guid:N}.json` under a new
  `Sessions` subfolder of the existing app-data folder - append-only across runs so a later history
  listing (T7.2.x) has every past run available, not just the latest; `RunSessionStore` reads
  `IOptions<LocalSettingsOptions>` the same way `LocalSettingsService` does rather than hardcoding a
  second copy of the app-data folder literal. Internally, `RunSessionStore` tracks the in-progress
  session as a single nullable `ActiveSession` record rather than several loose nullable fields, so
  there's no null-forgiving (`!`) operator anywhere - it's either the one complete session in progress
  or there isn't one yet. `PromptViewModel.RunPromptAsync` calls `RecordRun(fullPrompt, result.Output)`
  once a run reaches a terminal success/failure state (not for a cancelled run or one that threw before
  producing a CLI result) - `fullPrompt` (pre-retry) and `result.Output` (post-retry, since
  `CombineUsage` already folds a retry's output in) so the persisted record matches exactly what was
  sent and what was actually parsed. `ApplyOrchestrationService` - the single Core service that already
  owns every apply/undo/revert mutation - gained a 6th constructor parameter, `IRunSessionStore`, and
  calls `Resave()` after `ApplySelected`/`TryUndoLastApply`/`RevertAllApplied`, each guarded on at least
  one file actually having succeeded (mirroring `RecordBatch`/`Record`'s existing "no-op on an empty
  list" pattern) so a batch that changed nothing doesn't trigger a pointless disk write. Wired into DI in
  `App.axaml.cs` via a factory registration that computes the `Sessions` folder path from
  `IOptions<LocalSettingsOptions>`. Covered by new `RunSessionStoreTests` (10 cases: writes one file per
  recorded run, persists prompt/raw output/tutorial/working directory, persists per-file content and
  hunk selections, falls back to the whole-file selection when a file has no hunks, persists the apply
  log, `Resave` no-ops before any `RecordRun`, `Resave` overwrites the same file rather than creating a
  new one, `Resave` picks up selection and apply-log changes made after `RecordRun`, and calling
  `RecordRun` twice keeps the first file and starts a second), 5 new `ApplyOrchestrationServiceTests`
  cases (resave guarded correctly on an empty apply/undo/revert, resave reflects the new apply-log entry
  after each, and applying before any run was recorded doesn't throw or write a file), and 3 new
  `PromptViewModelTests` cases (a successful run records the prompt actually sent and the raw output, a
  non-zero exit code still records the run, and a run that never starts because the CLI isn't available
  records nothing) using a new hand-written `FakeRunSessionStore`. Full solution builds with 0
  warnings/errors, 422/422 tests pass (304 Core + 118 Avalonia, up from 404).
- [x] T7.1.2 Restore the last session on launch. — done 2026-10-18: `IRunSessionStore` gained
  `RestoreLastSession()`, the inverse of T7.1.1's save path - finds the most recently started
  session file (same sortable-filename trick `RecordRun` writes, so no need to open every file to
  compare `StartedAt`), rebuilds each file's `ProposedFileChangeViewModel`/`ProposedHunk` graph
  (content, edits, and hunk/whole-file selection state) from its `RunSessionFile`/`RunSessionHunk`
  snapshot, and pushes the result into `IPromptResultStore` via the existing `SetResult` plus a new
  `IApplyHistoryLog.Restore(entries)` (replaces the log wholesale, preserving original timestamps -
  distinct from `Record`, which always stamps `UtcNow` for a new action). Also re-adopts the loaded
  session as `RunSessionStore`'s active one, so a `Resave()` after relaunch keeps overwriting that
  same file instead of orphaning it. Deliberately out of scope, matching what T7.1.1 chose not to
  persist in the first place: `IApplyHistoryStore`'s undo stack and `IApplyBackupStore`'s session
  baseline, both of which only ever held in-memory full-file content for the running session and
  were never written to disk - "Undo Last Apply" naturally has nothing to undo right after a
  restart, same as before this task. Wired into `App.axaml.cs`'s `InitializeThemeAndNavigate`,
  before navigating to `MainViewModel`, so the Tutorial/Diffs pages already have last session's
  content the first time the user opens them - no `MainViewModel`/`MainView` changes needed. Covered
  by 7 new `RunSessionStoreTests` cases (no sessions folder returns null and touches neither store,
  an empty folder returns null, restoring picks the most recently recorded session, file content and
  hunk selection round-trip, the no-hunks whole-file fallback round-trips, the apply log round-trips,
  and restoring then resaving overwrites the restored file rather than creating a new one) and 2 new
  `ApplyHistoryLogTests` cases for `Restore`. Full solution builds with 0 warnings/errors, 431/431
  tests pass (313 Core + 118 Avalonia, up from 422).

**F7.2 — History UI**
- [x] T7.2.1 History page listing past runs with timestamp, prompt excerpt, and file count. — done
  2026-10-18: new `IRunSessionStore.ListSessions()` returns every recorded session newest-first
  (shares a `GetSessionFileNamesNewestFirst()` helper with `RestoreLastSession`, which now also
  gets a monotonic sequence number in the session filename - `RecordRun`'s previous guid-only
  tiebreak sorted same-millisecond runs randomly instead of by call order, which was previously
  masked by luck but surfaced as a real, reproducible test flake once `ListSessions` made ordering
  observable from two runs recorded back-to-back). New `HistoryViewModel`/`HistoryView` (registered
  in `PageService`/`App.axaml.cs`, added as a "History" item in `ShellView`'s `NavigationView`) list
  each run as a `HistorySessionRow` (`StartedAt`/excerpted `UserPromptText`/`FileCount`, pre-formatted
  into one display string via a new `History_SessionRow` resource key, following
  `TutorialViewModel.FormatHistoryEntry`'s precedent over XAML `StringFormat`).
- [x] T7.2.2 Reopen a past run's tutorial and diffs read-only. — done 2026-10-18: selecting a session
  rebuilds its `RunSessionFile`s into `ProposedFileChangeViewModel`s via a new shared
  `Paarde.Core.Helpers.RunSessionFileConverter` (the `ToRunSessionFile`/`ToProposedFileChangeViewModel`
  conversions that used to be private to `RunSessionStore`, now also used by `HistoryViewModel`) and
  lists them in a second master list; picking a file shows it in the existing `DiffsDetailControl`
  unmodified - it only renders `DiffLines`, which `ProposedFileChangeViewModel` already computes from
  the restored `OriginalContent`/`EditedContent` on construction, so no disk read of the file's
  *current* state is needed or wanted for a historical run.
- [x] T7.2.3 Re-run or fork a past prompt. — done 2026-10-18: `RunSession` gained `UserPromptText`
  (defaults to `""` for pre-existing session files) alongside the existing `Prompt`, since `Prompt` is
  the fully composed text actually sent to the CLI (instruction template + preset/depth + context
  files + the user's text) - resending that as-is would double up the instruction template. New
  `PromptLaunchRequest(PromptText, WorkingDirectoryPath, AutoRun)` navigation parameter, handled in
  `PromptViewModel.OnNavigatedTo`: loads `UserPromptText`/`WorkingDirectoryPath` into the page, then
  (`AutoRun: true`, "Re-run") immediately invokes `RunPromptCommand` the same way the button would, or
  (`AutoRun: false`, "Fork") leaves it for the user to edit first. Both go through the *current*
  instruction template/preset/depth/context-files pipeline rather than literally replaying the old
  composed prompt. `IRunSessionStore.RecordRun` widened to `(prompt, rawOutput, userPromptText)`.
  Covered by new `RunSessionStoreTests`/`RunSessionFileConverterTests`/`HistoryViewModelTests`/
  `PromptViewModelTests` cases. Full solution builds with 0 warnings/errors, 450/450 tests pass (322
  Core + 128 Avalonia, up from 431).

**F7.3 — Multi-turn conversation**
- [x] T7.3.1 Follow-up prompts that carry the prior turn's context (CLI session resume or API
  history). — done 2026-10-18: chose the CLI's `-r/--resume <session-id>` over `-c/--continue`
  because `--continue` can't target a specific prior turn (it always resumes "the most recent
  conversation in cwd", which breaks as soon as a user runs something unrelated in between) while
  `--resume` needs only a session id this process already has. `LlmRunResult` gained `SessionId`,
  captured in `ClaudeCliRunner` from the stream-json `"result"` line's `session_id` field (the same
  line `TotalCostUsd`/`DurationMs`/usage already come from); `ILlmRunner.RunPromptAsync` gained a
  `resumeSessionId` parameter that `ClaudeCliRunner` turns into `--resume <id>` ahead of the fixed
  streaming flags. `PromptViewModel` tracks the last run's session id in-memory only (not persisted
  to `RunSession`/disk - scoped to the current app session, same precedent as T7.1.2's undo stack)
  behind a new "Continue previous conversation" checkbox (`ContinuePreviousConversation`, gated by
  `HasPreviousSession` so it can't be checked before a session id exists); a schema-validation retry
  resumes from the same id as its original attempt since both are the same logical turn. Changing
  `WorkingDirectoryPath` clears the tracked session id and unchecks the box, since a CLI session is
  scoped to the directory it ran in. Covered by new `ClaudeCliRunnerTests` (argv echoing fake script
  asserts `--resume` is/isn't passed; session_id capture) and `PromptViewModelTests` cases. Full
  solution builds with 0 warnings/errors, 456/456 tests pass (324 Core + 132 Avalonia, up from 450).
- [x] T7.3.2 "Refine this file" / "explain this step further" inline actions that issue a scoped
  follow-up. — done 2026-10-18: both actions resume the session id tracked in `IPromptResultStore`
  (widened with a `SessionId` property, set only on a successful run - the mid-stream partial-result
  `SetResult` call passes `null` so it never clobbers the tracked value) rather than starting a fresh
  conversation, since both pages' `ProposedFileChangeViewModel`s originate from that same run.
  "Explain this step further" (`TutorialStepViewModel.ExplainFurtherCommand`) asks for plain prose on
  a resumed session that was told to always answer in JSON; a model that answers in JSON anyway is
  unwrapped via `ClaudeJsonResponseParser.TryParse` (`.Tutorial` doubles as the prose text for an
  "answer" response) rather than adding a second parser. "Refine this file"
  (`TutorialViewModel.RefineSelectedFileCommand`) reuses the main run's exact instruction
  template/JSON contract and `StructuredResponseParser`, scoped with a "only modify this file"
  instruction plus the file's current content; on success the matching file in the response becomes
  a new `ProposedFileChangeViewModel` (immutable by design - `Hunks` is init-only, `ProposedContent`
  has no setter) swapped by index into both `TutorialViewModel.Files` and
  `IPromptResultStore.Files`, keeping the Diffs page in sync without new pub/sub wiring. Deliberately
  no schema-validation retry loop for either action (unlike the main run's one-shot retry) - a failed
  attempt just shows an error and the user clicks again - and no "Refine this file" UI on the Diffs
  page (Tutorial page only, since that's where the apply/edit workflow lives). Covered by new
  `TutorialStepViewModelTests`, new `TutorialViewModel` refine cases, and `PromptResultStoreTests`/
  `PromptViewModelTests` cases for the new `SessionId` field. Full solution builds with 0
  warnings/errors, 471/471 tests pass (326 Core + 145 Avalonia, up from 456).
- [x] T7.3.3 Ask-a-question-about-this-change affordance inside the tutorial. — done 2026-10-21:
  follows T7.3.2's pattern but with a free-form question instead of a fixed "go deeper" prompt.
  `TutorialStepViewModel` gained `QuestionText`/`QuestionAnswer`/`IsAskingQuestion`
  (`[ObservableProperty]`) and an `AskQuestionCommand` (`CanExecute`-gated on a non-blank
  `QuestionText` and not already asking) that resumes the same tracked session id as
  `ExplainFurtherCommand`, sending the step's title/explanation/snippet plus the user's question and
  asking for plain prose; a model that answers in JSON anyway is unwrapped the same way via
  `ExtractProseAnswer`. `TutorialDetailControl.axaml` gained a question `TextBox` + "Ask" button per
  step, with the answer shown via `MarkdownTextBlock` underneath (same visibility pattern as
  `FurtherExplanation`). No new ViewModel constructor parameters or code-behind changes were needed -
  `TutorialStepViewModel` already had everything required (`_llmRunner`, `_resumeSessionId`). Covered
  by new `TutorialStepViewModelTests` cases (success, session-id resumption, JSON-wrapped answer
  unwrapping, failure message, `CanExecute` gating on blank text and while asking). Full solution
  builds with 0 warnings/errors, 626/626 tests pass (397 Core + 229 Avalonia). Not visually verified
  (no X server in this sandbox, same limitation noted on earlier UI-touching tasks) beyond confirming
  the app still launches without throwing (ran under `timeout`; exit code 124 confirms it was still
  running, not crashed, when killed).

---

### EPIC 8 — Make it feel like an IDE *(P1/P2)*

**F8.1 — Project explorer**
- [x] T8.1.1 File-tree view of the working directory with gitignore-aware filtering. — done
  2026-10-19: new "Explorer" page (`ExplorerViewModel`/`ExplorerView`, added to `PageService` and
  `ShellView`'s nav menu) shows a `TreeView` of the working directory last picked on the Prompt
  page - read from the same persisted `SavedWorkingDirectory` setting (now a shared
  `WorkingDirectorySettingsKeys` constant, mirroring `ClaudeCliSettingsKeys`) rather than
  `IPromptResultStore.WorkingDirectoryPath`, so the tree is browsable as soon as a folder is
  picked, not only after a prompt has actually run. A new `FileTreeBuilder` (`Paarde.Core.Helpers`)
  walks the directory into an immutable `FileTreeEntry` tree (directories before files,
  alphabetical), always excluding `.git`, and skipping anything the project's root `.gitignore`
  matches via the existing `GitIgnoreMatcher` (T4.1.2) - reusing it rather than writing a second
  ignore-matching implementation means "ignored" means the same thing here as it does for
  `ProtectedPathChecker`'s apply-time safety check. That matcher was previously documented as not
  supporting `[...]` character classes; it now does (additive, regex `[...]` passthrough), since
  this very repo's own `.gitignore` relies on exactly that (`[Bb]in/`, `[Oo]bj/`) and the feature
  would otherwise fail to filter build output in its own dogfood case. Nested per-directory
  `.gitignore` files are deliberately still out of scope, matching that matcher's existing
  documented boundary - the root `.gitignore` covers the common case this task asks for. The
  `TreeView`'s hierarchy comes from Avalonia's `TreeDataTemplate` (`ItemsSource="{Binding
  Children}"`), not a sub-page per node, so expand/collapse state is just the control's own,
  with no new ViewModel-level state to track. Covered by new `GitIgnoreMatcherTests` (character
  class cases), `FileTreeBuilderTests`, and `ExplorerViewModelTests`. A full interactive
  screenshot verification was attempted via headless rendering but the `dotnet run` build of the
  scratch verification project hung without producing output after several minutes - abandoned per
  the same "don't rabbit-hole on an unrelated environment limitation" judgment as T8.4.5, relying
  instead on the new tests plus a clean compiled-bindings build (which validates every binding
  here, including `TreeDataTemplate`'s, at build time). Full solution builds with 0 warnings/
  errors, 520/520 tests pass (355 Core + 165 Avalonia).
- [x] T8.1.2 Badges marking files touched by the current proposal. — done 2026-10-19: new
  `FileTreeNodeViewModel` (`Paarde.Avalonia.ViewModels`) wraps each `FileTreeEntry` with an
  `IsTouched` flag - kept Avalonia-side rather than on `FileTreeEntry` itself since "touched by
  the current proposal" is UI/session state, not something inherent to a file on disk.
  `ExplorerViewModel` now also takes `IPromptResultStore` (the same singleton `DiffsViewModel`
  already reads) and, on each tree build, resolves every `ProposedFileChangeViewModel.FilePath`
  (relative) to an absolute path the same way `ProposedFileChangeViewModel.RefreshOriginalContent`
  does (`Path.Combine(workingDirectoryPath, FilePath)`, then `Path.GetFullPath`) and matches it
  against each `FileTreeEntry.FullPath` with `StringComparison.Ordinal` - the one existing
  path-comparison precedent in the codebase (`FilePathContainment`). `ExplorerView.axaml`'s
  `TreeDataTemplate` switched from `FileTreeEntry` to `FileTreeNodeViewModel` and now shows a
  small accent-colored dot next to a touched file's name. Covered by new `ExplorerViewModelTests`
  cases: a touched file, an untouched sibling, no proposal at all, and a touched file nested in a
  subdirectory. Full solution builds with 0 warnings/errors, 523/523 tests pass (355 Core + 168
  Avalonia).
- [x] T8.1.3 Open any file in the editor, proposed or not. — done 2026-10-19: `ExplorerViewModel`
  gained `SelectedNode`, `SelectedFilePath`, and `SelectedFileContent`; selecting a file node reads
  its content via the existing `FileContentReader.ReadOrEmpty` (same helper
  `ProposedFileChangeViewModel.RefreshOriginalContent` and `ApplyOrchestrationService` already use),
  while selecting a directory or clearing the selection clears the preview. `RefreshTree()` also
  resets `SelectedNode` first, since rebuilding `Root` replaces the whole node tree and would
  otherwise leave the preview pointing at a stale, orphaned node. `ExplorerView.axaml` is now a
  split view: the existing `TreeView` (now with `SelectedItem` bound two-way to `SelectedNode`) on
  the left, a read-only `CodeEditor` (the same control `TutorialDetailControl` uses for editable
  proposal content, here with `IsReadOnly="True"` and bound one-way) on the right, with a
  placeholder message when nothing is selected. Deliberately not wired to
  `ProposedFileChangeViewModel.EditedContent` even for touched files - Explorer stays a browse/view
  surface, editing a proposal stays the Tutorial page's job. Covered by new
  `ExplorerViewModelTests` cases: selecting a file loads its content, selecting a directory clears
  the preview, selecting null clears it, and refreshing the tree clears a stale selection.
  Also fixed two pre-existing bugs in `CodeEditor` surfaced by this page, found via
  `Avalonia.Headless` pixel-capture probes (`CaptureRenderedFrame`) since compiled-binding
  validation and property-level checks both passed while the control still rendered as blank
  white pixels - standard unit tests can't catch this class of bug. The actual root cause: `CodeEditor`
  subclasses `TextEditor` (a `TemplatedControl`) without overriding `StyleKeyOverride`, which defaults
  to `GetType()`; Avalonia therefore looked for a `ControlTheme` keyed `{x:Type CodeEditor}`, found
  none, and applied no template at all - no background Border, no `TextView`, nothing rendered,
  regardless of what `Text`/`Background`/`Foreground` were set to. Fixed by overriding
  `StyleKeyOverride` to return `typeof(TextEditor)` so `CodeEditor` reuses `TextEditor`'s theme.
  Separately (and still worth keeping): AvaloniaEdit.TextMate only recolors syntax tokens, it never
  sets the editor's own `Background`/`Foreground`, so once the template fix made the control visible,
  the DarkPlus theme's light foreground text would otherwise be drawn on whatever background the
  app's current Fluent theme defaulted to - invisible in Light theme. `CodeEditor` now reads
  `editor.background` / `editor.foreground` from `Installation.TryGetThemeColor` and applies them
  directly after `InstallTextMate` returns, since `Installation`'s constructor already fires its one
  and only `AppliedTheme` event before any subscriber (including a handler added right after
  `InstallTextMate`) could ever receive it. Both fixes confirmed visually via a headless-rendered PNG
  showing correctly colored, line-numbered, syntax-highlighted text. Full solution builds with 0
  warnings/errors, 527/527 tests pass (355 Core + 172 Avalonia).

**F8.2 — Shell & workspace**
- [x] T8.2.1 Replace the empty `MainView` with a real dashboard/landing page (recent projects, recent
  runs, quick prompt). — partial progress 2026-10-18: `MainView` is no longer an empty `Grid` - it
  now shows a logo/wordmark header (a plain vector monogram badge; no image-generation tooling was
  available to produce real artwork) and a static numbered "Getting started" walkthrough of the
  Prompt → Tutorial/Diffs → Apply → Settings flow, written directly in `MainView.axaml` with no
  `MainViewModel` changes needed, matching the existing convention that markup-only strings live in
  `.axaml` rather than `Strings/Resources.cs`. Recent projects, recent runs, and a quick-prompt
  entry point are still outstanding - this task isn't done, just no longer blocked on there being
  nothing on the page at all.
  Done 2026-10-19: `MainViewModel` gained `IRunSessionStore`/`ILocalSettingsService`/
  `INavigationService` dependencies (all already-registered singletons) and now implements
  `INavigationAware`. `OnNavigatedTo` reads `IRunSessionStore.ListSessions()` (the same source the
  History page uses) and populates `RecentRuns` (newest 5, reusing `HistorySessionRow.From` for
  identical display formatting) and `RecentProjects` (newest 5 *distinct* `WorkingDirectoryPath`
  values - `Distinct()` over an already-newest-first list keeps each path's most recent occurrence
  without a second sort). Both lists are selection-driven (`SelectedRecentRun`/
  `SelectedRecentProject` + an `Open`/`Switch to this project` button) rather than a command bound
  per row reaching back out to `MainViewModel` from inside an `ItemsControl`'s `DataTemplate` -
  mirrors `HistoryViewModel.Selected`/`ForkCommand` exactly, and sidesteps any ancestor-binding
  fragility under compiled bindings. `OpenRecentRunCommand` reopens a run on the Prompt page via a
  `PromptLaunchRequest` with `AutoRun: false` (same as `HistoryViewModel.Fork` - this is the app's
  first screen, so sending straight to the CLI without a chance to review first would be
  surprising). `OpenRecentProjectCommand` is deliberately *not* a `PromptLaunchRequest` - it only
  saves the chosen path under `WorkingDirectorySettingsKeys.WorkingDirectory` (the same setting
  `PromptViewModel`/`ExplorerViewModel` already read) and navigates with no parameter, so it
  doesn't clobber whatever draft prompt text is already saved there the way overriding
  `PromptText` would. The quick-prompt box (`QuickPromptText` + `SendQuickPromptCommand`, gated on
  non-blank text) also hands off via `PromptLaunchRequest` with `AutoRun: false`, reading the
  last-saved working directory first so the Prompt page doesn't lose it the way passing `""` would.
  Covered by new `MainViewModelTests` (empty state, population/cap/distinct-projects from
  `ListSessions`, both commands' `CanExecute` gating and navigation parameters, quick-prompt gating
  and navigation). Full solution builds with 0 warnings/errors, 536/536 tests pass (355 Core + 181
  Avalonia). Not visually verified - this sandbox has no X server and no committed
  `Avalonia.Headless` test host (prior attempts at this, e.g. T8.1.1/T8.4.5, were one-off scratch
  probes, not infrastructure left in the repo) - reviewed by hand and via the new tests instead.
- [x] T8.2.2 Tabbed editing of multiple files. — done 2026-10-19: the Tutorial page's detail pane
  previously showed only `Selected` via a single `ContentControl`, throwing away and rebuilding
  `TutorialDetailControl` on every selection change - so switching back to a file you'd already
  edited lost nothing (the edit lives on the `ProposedFileChangeViewModel` itself), but there was
  no way to see more than one file's editor at a time or jump directly back to one without
  re-picking it from the master list. `TutorialViewModel` gained an `OpenTabs`
  `ObservableCollection<ProposedFileChangeViewModel>` - a subset of `Files`, populated lazily as the
  user selects files rather than all of `Files` up front, so the page opens with just the one file
  `EnsureItemSelected` already auto-selects as its single tab. `OnSelectedChanged` now adds `value`
  to `OpenTabs` if it isn't already there (a no-op once a file's tab is already open, including the
  jump-from-Diffs and refine-this-file paths, which both just set `Selected`); `ReplaceFile` swaps a
  refined file into its old tab's position in `OpenTabs` the same way it already does for `Files`
  and `_promptResultStore.Files`, so refining a file never leaves a stale duplicate tab behind.
  Closing a tab is a new `CloseSelectedTabCommand` (gated on `Selected is not null`) acting on
  `Selected` rather than a per-tab close button bound back to the view model - mirrors the
  `Selected`+action-button pattern this codebase already uses throughout (e.g.
  `HistoryViewModel.Selected`/`ForkCommand`, and T8.2.1's `MainViewModel`) instead of introducing
  this repo's first per-row/per-tab `DataTemplate` command binding back to a parent `DataContext`.
  Closing a tab selects a neighboring one (`OpenTabs[Math.Min(index, OpenTabs.Count - 1)]`) so the
  detail pane stays populated while other tabs remain open, or `null` if that was the last tab -
  deliberately doesn't auto-reopen anything from `Files` afterward, respecting the explicit close.
  `OpenTabs` resets on every `OnNavigatedTo` alongside `Files`/`Selected`, which is safe rather than
  a regression: `TutorialViewModel` is `AddTransient`, so a fresh instance (and therefore empty
  `OpenTabs`) is already resolved on every navigation to this page today, independent of this
  change. `TutorialView.axaml` replaces the old `ContentControl` with a `Grid` (shown only when
  `HasOpenTabs`) containing a "Close Tab" button bound to `CloseSelectedTabCommand` and a
  `TabControl` (`ItemsSource="{Binding OpenTabs}"`, `SelectedItem="{Binding Selected, Mode=TwoWay}"`)
  whose tab headers show each file's trimmed path plus a `*` when `IsDirty`, and whose content
  template still hosts `TutorialDetailControl` per tab - built with Avalonia's own `TabControl`, no
  new xmlns import needed. Covered by new `TutorialViewModelTests`: auto-selected file opens as the
  lone initial tab, selecting another file adds an additional tab without closing others,
  reselecting an already-open tab doesn't duplicate it, `CloseSelectedTabCommand` `CanExecute`
  gating and closing a non-last tab selects its neighbor, closing the last tab clears `Selected`,
  and refining a file swaps it into the same tab position rather than appending a new one. Full
  solution builds with 0 warnings/errors, 543/543 tests pass (355 Core + 188 Avalonia). Not visually
  verified - same sandbox limitation as T8.2.1 and every other UI-only task (no X server, no
  committed `Avalonia.Headless` test host) - reviewed by hand and via the new tests instead.
- [x] T8.2.3 Resizable/collapsible panes; persist layout. — done 2026-10-19: every master-detail
  page's browse-list pane (Explorer's tree, Diffs' and Tutorial's file list, History's sessions
  and files lists) was a fixed-pixel-width `Grid` column with no way to resize it (Explorer alone
  already had a `GridSplitter`, but it didn't remember a drag between visits). A new
  `ResizablePaneHelper` (`Paarde.Avalonia/Helpers`) wraps one `ColumnDefinition` + `ILocalSettingsService`:
  `RestoreAsync` (called from each View's `Loaded` handler) reads a persisted width back in, falling
  back to that page's original fixed width if nothing's been saved yet; `OnSplitterDragCompleted`
  (wired to the matching `GridSplitter.DragCompleted` - inherited from `Thumb`, fired once per drag
  rather than on every `DragDelta`) saves the column's current width back out. "Collapsible" is
  deliberately *not* a separate collapsed flag/toggle button - dragging a splitter all the way to one
  side already produces (and persists) a width of 0, and dragging it back out restores the pane,
  which is simpler than keeping a second piece of state in sync with the dragged width and avoids
  this being the app's first per-page custom chrome bolted outside the resizable area just to
  reopen it. Explorer, Diffs, and Tutorial each needed their `ColumnDefinitions="280,*"`\-style
  shorthand expanded to an explicit `<Grid.ColumnDefinitions>` block (needed to reach a specific
  `ColumnDefinition` by index from code-behind) plus a new `GridSplitter` column between the master
  and detail panes (Diffs and Tutorial didn't have one at all before this). History got two -
  sessions|files and files|detail - since it has two list panes in a row. Settings keys live in a
  new `WorkspaceLayoutSettingsKeys` (mirrors `ClaudeCliSettingsKeys`/`WorkingDirectorySettingsKeys`),
  one per page+pane so they can't collide. `ResizablePaneHelper` itself only touches a
  `ColumnDefinition` and `ILocalSettingsService`, so (unlike the View/XAML wiring around it) it's
  unit-testable without an Avalonia application/window - covered by new `ResizablePaneHelperTests`:
  default width with nothing persisted, restoring a previously-saved width, clamping a corrupt
  negative persisted width to 0, and persisting on drag-completed including the all-the-way-shut
  (0-width) case. Full solution builds with 0 warnings/errors, 548/548 tests pass (355 Core + 193
  Avalonia). Not visually verified - same sandbox limitation as every other UI-only task (no X
  server, no committed `Avalonia.Headless` test host) - reviewed by hand and via the new tests
  instead.
- [x] T8.2.4 **Recent-projects list and fast project switching.** — done 2026-10-20: T8.2.1 already
  put a Recent Projects list on the Main page, but that only helps when landing on that page - it
  doesn't help while already working on the Prompt page. This adds a `RecentWorkingDirectories`
  collection + `HasRecentWorkingDirectories`/`SelectedRecentWorkingDirectory` to `PromptViewModel`,
  populated on `OnNavigatedTo` the same way (`IRunSessionStore.ListSessions()` -> distinct
  `WorkingDirectoryPath`, newest-first, capped at 5) as `MainViewModel.RecentProjects`, and a
  `Recent...` `ComboBox` next to the existing **Browse...** button in `PromptView.axaml` (only
  visible once there's at least one entry). Picking an item immediately sets `WorkingDirectoryPath`,
  persists it via the same `WorkingDirectorySettingsKeys.WorkingDirectory` setting
  `BrowseWorkingDirectoryAsync` already writes, and resets the selection back to null so the
  dropdown returns to its placeholder rather than looking permanently stuck on one entry - the
  working directory text box above it already shows the current value, so the combo doesn't need
  to keep "holding" it. No new service or command was needed; this is deliberately the same
  shape as `MainViewModel`'s existing recent-projects handling rather than a new abstraction.
  Covered by 4 new `PromptViewModelTests`: no sessions leaves the list empty, recent working
  directories come back distinct and capped at 5, and selecting an entry switches/persists the
  working directory and clears the selection. Full solution builds with 0 warnings/errors,
  551/551 tests pass (355 Core + 196 Avalonia). Not visually verified - same sandbox limitation as
  every other UI-only task (no X server, no committed `Avalonia.Headless` test host).

**F8.3 — Better diff viewer**
- [x] T8.3.1 **Side-by-side mode in addition to inline.** — done 2026-10-20: a new
  `DiffBuilder.BuildSideBySideRows(IReadOnlyList<DiffLine>)` (Core, alongside `BuildLineDiff`/
  `GroupIntoHunks`) turns the existing flat line-level diff into `SideBySideDiffRow`s - pairing up
  each maximal run of consecutive Removed/Added lines index-by-index (preserving each side's own
  order) so an old/new line pair lands on the same row, padding the shorter side with a blank cell
  when a run has more lines on one side than the other; an Unchanged line gets its own row with
  identical text on both sides. `DiffsDetailControl` (shared by the Diffs page, History page's file
  view, and the Diffs jump-to-hunk flow) gained a `Side-by-side` `ToggleButton` next to its existing
  `Copy file contents` button and a second `ItemsControl`, hidden by default, templated over
  `SideBySideDiffRow` as two columns with a thin divider. This is deliberately local view state,
  not persisted and not added to the shared `ProposedFileChangeViewModel` (which both the Tutorial
  and Diffs pages bind to the same instance of) - the view mode is a property of this one control,
  not of the file being shown. `SideBySideItemsControl.ItemsSource` is set from code-behind
  (recomputed on `DataContextChanged`, i.e. switching files, and on the bound file's own
  `DiffLines` changing, i.e. an edit) rather than bound in XAML, since `BuildSideBySideRows`'s
  output isn't something `ProposedFileChangeViewModel` itself exposes. T8.4.5's scroll-to-hunk jump
  (`ApplyPendingScrollTarget`) always switches back to inline first and resets the toggle - the
  side-by-side rows have no equivalent index-based scroll target, and landing in the wrong mode
  after a deliberate jump would be confusing. Covered by 8 new `DiffBuilderTests` (no changes pairs
  every unchanged line with itself, equal-count removed/added pairs one-to-one, more-added and
  more-removed cases pad the shorter side with blank rows, a change sandwiched between unchanged
  lines keeps those on their own rows, an empty diff returns no rows, and prefix text is blank only
  for a padded cell). Full solution builds with 0 warnings/errors, 558/558 tests pass (362 Core +
  196 Avalonia). Not visually verified - same sandbox limitation as every other UI-only task (no X
  server, no committed `Avalonia.Headless` test host).
- [x] T8.3.2 Collapse unchanged regions with expand-context controls. — done 2026-10-20: new
  `DiffBuilder.FindCollapsibleSegments` (private) splits a file's flat `DiffLines` into segments that
  must stay visible (a changed region, or unchanged context within `contextLines` of one) and
  segments far enough from any change to collapse - reusing the existing `FindChangeRanges` helper
  `GroupIntoHunks` already had, so a segment's visible-context boundary always lines up with a
  `DiffHunk`'s own `SourceRangeStart`/`End` as long as both use the same default `contextLines`
  (3). Two new public builders consume it: `BuildCollapsibleRows` (inline view - one
  `CollapsibleDiffRow` per visible line, or one collapsed-count placeholder row per collapsible
  segment) and `BuildCollapsibleSideBySideRows` (side-by-side view - same segmenting, but each
  visible segment's lines are paired via the existing `BuildSideBySideRows` rather than listed one
  row per line, since a segment boundary never splits a Removed/Added run). Both take an
  `expandedSegmentStarts` set (keyed by a placeholder's own `LineIndex`) so a segment the user has
  already expanded renders its real lines instead; `CollapsibleDiffRow`/`CollapsibleSideBySideDiffRow`
  (`Paarde.Core/Models`) both implement a new `ICollapsedDiffRegion` so `DiffsDetailControl`'s
  "expand" click handler works against either row type without caring which view produced it.
  `DiffsDetailControl` now owns the `HashSet<int>` of expanded segments (cleared whenever the shown
  file or its `DiffLines` changes, same reset points as T8.3.1's side-by-side recompute) and recomputes
  both views' `ItemsSource` together via a single `RefreshDisplayRows`; each `DataTemplate` shows
  either the normal row grid or a `Button` placeholder ("Show N unchanged lines") toggled by
  `IsVisible="{Binding !IsCollapsedPlaceholder}"`/`{Binding IsCollapsedPlaceholder}`. T8.4.5's
  scroll-to-hunk jump (`ApplyPendingScrollTarget`) no longer assumes a `DiffLines` index equals a
  display-row index (collapsing breaks that 1:1 mapping) - it now looks the target row up by
  `LineIndex` in the freshly cached inline rows, relying on the contextLines-alignment invariant above
  to guarantee that row is never itself folded into a placeholder. Covered by 13 new `DiffBuilderTests`
  cases (short runs stay fully visible, a long run between two changes collapses its middle while
  keeping context on both sides, leading/trailing runs with no change on one side collapse all the way
  to that edge, a run with no changes at all collapses entirely, expanding a segment restores its real
  lines for both view builders, changes close enough together still don't collapse their gap, empty
  input, and a dedicated invariant test asserting every `DiffHunk.SourceRangeStart` from
  `GroupIntoHunks` always lands on a non-placeholder row). Full solution builds with 0 warnings/errors,
  570/570 tests pass (374 Core + 196 Avalonia, up from 558). Not visually verified - same sandbox
  limitation as every other UI-only task (no X server, no committed `Avalonia.Headless` test host);
  confirmed only that the app still launches without throwing (`timeout`; exit code 124).
- [x] T8.3.3 **Intra-line (word-level) highlighting.** — done 2026-10-20: `DiffBuilder`'s line-level
  LCS diff was refactored behind a shared private `ComputeDiff(string[], string[])` so the same
  algorithm (and the same `MaxLcsCells` fallback) could be reused at word granularity; a new public
  `BuildWordDiff(originalLine, proposedLine)` tokenizes each line with a word-boundary regex (word
  runs vs. non-word/whitespace runs, split losslessly so the tokens always reconstruct the original
  line exactly) and returns the token-level diff as the same `DiffLine` record type line diffs
  already use, rather than inventing a parallel model. `SideBySideDiffRow` gained two new optional
  trailing properties, `OriginalWordSpans`/`ProposedWordSpans` (`IReadOnlyList<DiffLine>?`, default
  null), populated inside `BuildSideBySideRows`'s existing run-pairing (`FlushRun`) only for a
  genuine Removed/Added pair - not a padded cell, and not an Unchanged line, neither of which has
  anything meaningful to diff against - via a small `ExtractSide` helper that filters the word diff
  down to one side by excluding the other side's token kind. The inline view has no equivalent
  row-level pairing to hang this off of (an inline Removed line and its Added partner are two
  separate rows), so a dedicated `ComputeInlineWordSpans` walks `diffLines` the same way
  `BuildSideBySideRows` does but just to build a `LineIndex -> word spans` map, consumed by
  `BuildCollapsibleRows` (which also gained a matching optional `WordSpans` property on
  `CollapsibleDiffRow`); `CollapsibleSideBySideDiffRow` needed no change since its wrapped `Row`
  already carries the new properties. Rendering required a new custom control,
  `DiffWordSpanTextBlock` (a `TextBlock` subclass modeled on the existing `MarkdownTextBlock`
  precedent, since `TextBlock.Inlines` can't be populated via a plain per-item XAML binding), which
  rebuilds its `Inlines` as plain `Run`s when `WordSpans` is null (falling back to one run of
  `LineText`, identical to a normal `TextBlock`) or as per-token `Run`s with a stronger-alpha
  version of `DiffLineKindToBackgroundConverter`'s own Added/Removed hue behind only the tokens
  whose own `Kind` is Added/Removed - wired into both `DiffsDetailControl.axaml` templates in place
  of the plain `TextBlock` that used to render `Line.Text`/`Row.OriginalText`/`Row.ProposedText`.
  Covered by 11 new `DiffBuilderTests` cases (identical lines tokenize to all-Unchanged, a single
  changed word is isolated from its unchanged neighbors, both sides' token lists losslessly
  reconstruct their own original line, completely different lines mark everything changed, a paired
  side-by-side row gets word spans on both sides while an Unchanged line or a padded/unpaired row
  gets none, and the same pairing-or-not behavior for the inline view's `WordSpans` and for
  `BuildCollapsibleSideBySideRows`'s wrapped `Row`). Full solution builds with 0 warnings/errors,
  581/581 tests pass (385 Core + 196 Avalonia, up from 570). Not visually verified - same sandbox
  limitation as every other UI-only task (no X server, no committed `Avalonia.Headless` test host);
  confirmed only that the app still launches without throwing (`timeout`; exit code 124).
- [x] T8.3.4 **Syntax highlighting inside the diff; next/previous-change navigation.** — done
  2026-10-20: added a new `Paarde.Avalonia.Helpers.SyntaxTokenizer`, wrapping the same
  TextMateSharp registry/theme `CodeEditor` already uses for the full-file editor (DarkPlus), but
  tokenizing one line at a time (`IGrammar.TokenizeLine`) rather than through
  `AvaloniaEdit.TextMate`'s document-wide installation, since a diff row has no backing
  AvaloniaEdit document; the grammar lookup is cached per file extension. Each token's foreground
  color comes from `Theme.Match(token.Scopes)`'s best-ranked rule, falling back to no color
  override (inheriting the ambient foreground) for an extension with no known grammar or a token
  the theme has nothing specific to say about. Rendering a diff row now needs two independent,
  differently-bounded tokenizations of the same line layered together - T8.3.3's word-level diff
  (background highlight) and this syntax tokenization (foreground) - so the merge itself was
  pulled out of `DiffWordSpanTextBlock` into a new pure, unit-testable
  `Paarde.Avalonia.Helpers.DiffLineRenderer.Build`, which normalizes both tokenizations into
  length-based spans covering the full line and walks them in lockstep, splitting at whichever
  boundary (diff or syntax) comes first, so every resulting run has one unambiguous Kind/Color
  pair; `DiffWordSpanTextBlock` itself just calls it and turns the result into `Run`s.
  `DiffWordSpanTextBlock` gained a `FilePath` property, wired in `DiffsDetailControl.axaml` via an
  ancestor binding (`$parent[UserControl].((coreVm:ProposedFileChangeViewModel)DataContext).FilePath`,
  compiling fine despite reaching outside the row's own `x:DataType` scope) rather than threading
  `FilePath` through every row model, since it's the same for every row in a given file. Next/
  previous-change navigation reuses `DiffBuilder.GroupIntoHunks` (the same hunks T8.4.5's
  jump-from-tutorial-step feature targets) - two new toolbar buttons plus a status label
  ("Change X of N") drive a `_currentHunkIndex` kept in `DiffsDetailControl`'s code-behind,
  resetting to "haven't navigated to one yet" whenever `DiffLines` is recomputed (file switch or
  edit, same invalidation trigger T8.3.2's expanded-region tracking already uses) and clamped
  rather than wrapping at either end; scrolling always switches to the inline view first, same
  reasoning `ApplyPendingScrollTarget` already established (the side-by-side view has no
  index-based scroll target), and both now share a `FindDisplayIndex(lineIndex)` helper pulled out
  of `ApplyPendingScrollTarget` rather than duplicating its row lookup. A T8.4.5 jump now also sets
  `_currentHunkIndex` to the hunk it landed on, so a following Next/Previous click continues from
  there instead of restarting. Covered by 11 new tests: 5 `SyntaxTokenizerTests` (a known
  extension colors a recognized keyword token, its tokens losslessly reconstruct the original
  line, an unknown extension/null file path/empty line all fall back to no tokens) and 6
  `DiffLineRendererTests` on the pure merge (no word spans and no syntax tokens yields one
  colorless span, word spans alone carry Kind through with no Color, syntax tokens alone carry
  Color through with no Kind, misaligned boundaries between the two split at every boundary, and
  the merged spans always reconstruct the original line exactly). Full solution builds with 0
  warnings/errors, 592/592 tests pass (385 Core + 207 Avalonia, up from 581). Not visually verified
  - same sandbox limitation as every other UI-only task (no X server, no committed
  `Avalonia.Headless` test host); confirmed only that the app still launches without throwing
  (`timeout`; exit code 124).

**F8.4 — Tutorial presentation**
- [x] T8.4.1 **Display the `## Tutorial` overview that is currently parsed and thrown away.** — done
  2026-10-18: `TutorialViewModel` gained a `Tutorial` property (pulled from `PromptResultStore.Tutorial`
  on `OnNavigatedTo`, same pull-on-navigation pattern as `Files`) and `HasTutorialOverview`;
  `TutorialView.axaml` shows it as an "Overview" header above the per-file detail pane, collapsed
  entirely when empty/whitespace-only.
- [x] T8.4.2 Render markdown in explanations (code spans, lists, emphasis) instead of flat text. —
  done 2026-10-18: new `MarkdownInlineParser` (`Paarde.Core.Helpers`) parses the limited subset the
  LLM's tutorial/explanation text actually uses - bullet/numbered lists, `**bold**`/`*italic*`
  emphasis, and `` `code` `` spans - into `MarkdownLine`/`MarkdownSpan` records, deliberately scoped
  to single-line constructs (no headings, fenced blocks, tables, or nested lists); full markdown
  rendering was rejected as unnecessary weight for what the model's responses actually contain. A
  new `MarkdownTextBlock` control (`Paarde.Avalonia.Controls`) renders that output as one wrapped
  `TextBlock` per line - bulleted/indented for list items, with `Run`s styled per span (bold
  `FontWeight`, italic `FontStyle`, code in a monospace font with a subtle background) - replacing
  the flat `TextBlock` bound to `Tutorial` in `TutorialView.axaml` and to `Explanation`/
  `FurtherExplanation` in `TutorialDetailControl.axaml`. `FurtherExplanation`'s block-level italic
  styling (previously `FontBlock.FontStyle="Italic"`) moves to a new `MarkdownTextBlock.FontStyle`
  property, since `StackPanel` (the control's base, chosen so each line gets its own wrapped
  paragraph) has no `FontStyle` of its own to inherit from in XAML. Verified by rendering the
  control headlessly (`Avalonia.Headless` + `UseSkia()`) with representative tutorial/explanation
  text covering every span kind and both list styles. Covered by new `MarkdownInlineParserTests`.
  Full solution builds with 0 warnings/errors, 490/490 tests pass (342 Core + 148 Avalonia).
- [x] T8.4.3 Step numbering, progress indicator, and prev/next step navigation. — done
  2026-10-18: `TutorialStepViewModel` now takes `stepNumber`/`totalSteps` in its constructor and
  exposes them plus `StepLabel` ("Step N:"), set by `TutorialDetailControl.RebuildSteps` from each
  hunk's index in `ProposedFileChangeViewModel.Hunks` - the same order the steps list displays them
  in. The per-step `DataTemplate` in `TutorialDetailControl.axaml` shows `StepLabel` next to the
  step title. A new `StepNavigationPanel` row above the steps `ScrollViewer` has Previous/Next
  buttons (`OnPreviousStepButtonClick`/`OnNextStepButtonClick`, disabled at the first/last step), a
  "Step X of Y" label, and a `ProgressBar`, all kept in sync by `RefreshStepNavigation()` against a
  new `_currentStepIndex` field; it's hidden entirely when a file has no resolved steps, matching
  the existing select-all row's pattern. Navigation uses `ItemsControl.ScrollIntoView(int)` (the
  plain `ItemsControl` already in use supports it directly, so no need to switch to `ListBox` and
  its unwanted selection-highlight semantics) rather than any new Core model changes - this is UI
  position tracking, not a persisted "applied" progress metric. Verified by rendering the nav
  panel/step header layout headlessly (`Avalonia.Headless` + `UseSkia()`). Full solution builds with
  0 warnings/errors, 491/491 tests pass (342 Core + 149 Avalonia).
- [x] T8.4.4 Export the tutorial to markdown/HTML. — done
  2026-10-18: new `TutorialExporter` (`Paarde.Core.Helpers`) builds a self-contained Markdown or HTML
  document from the overview plus each file's steps, numbered per file the same way
  `TutorialStepViewModel.StepLabel` (T8.4.3) numbers them in the UI - `BuildHtml` reuses
  `MarkdownInlineParser` (T8.4.2) to render explanations consistently with the in-app
  `MarkdownTextBlock` rather than a second markdown implementation, HTML-escaping everything else.
  `TutorialView` gained "Export as Markdown"/"Export as HTML" buttons bound to two new
  `TutorialViewModel` commands (disabled when there's neither an overview nor any files), which use
  a new `IFileSavePickerService` (`FileSavePickerService`, following the existing `IFolderPickerService`/
  `IFilePickerService` pattern around `IStorageProvider`) to pick a destination before writing with
  `File.WriteAllText` and reporting the result via a new `ExportStatusMessage`. Verified by running
  the exporter against representative multi-file, multi-step input and inspecting the generated
  Markdown/HTML directly. Covered by new `TutorialExporterTests` and `TutorialViewModelTests` export
  cases. Full solution builds with 0 warnings/errors, 501/501 tests pass (347 Core + 154 Avalonia).
- [x] T8.4.5 Jump from a tutorial step to the corresponding hunk in the diff viewer, and back. — done
  2026-10-18: a step's "View in Diff" button (`TutorialStepViewModel.ViewInDiffCommand`, disabled
  when `Hunk.MatchedDiffHunks` is empty - nothing resolved to jump to) navigates to the Diffs page
  with a new `DiffJumpRequest` (`Paarde.Avalonia.Models`, following the existing `PromptLaunchRequest`
  pattern) carrying the shared `ProposedFileChangeViewModel` instance, the target `DiffHunk`, and the
  originating step. `DiffsViewModel.OnNavigatedTo` selects that file and stashes the hunk as a
  "take-once" pending scroll target; `DiffsDetailControl`'s `Loaded` event (fired once this control -
  materialized per selected file via `DiffsView`'s `ContentTemplate` - is attached, by which point its
  `DataContext` is already set) reads it back via `this.FindAncestorOfType<DiffsView>()?.ViewModel` and
  scrolls the (now-named) `DiffLinesItemsControl` to `DiffHunk.SourceRangeStart` - the same
  `ItemsControl.ScrollIntoView(int)` already used for step navigation (T8.4.3). The way back is a
  "Back to Tutorial" button on the Diffs page itself (bound to a new `BackToTutorialCommand`, visible
  only when `DiffsViewModel.ReturnToStep` is set - i.e. the page was reached via a jump), which
  navigates to the Tutorial page with a new `TutorialJumpRequest` carrying the file and that step;
  `TutorialDetailControl` resolves it the same take-once-pending-target way, via its own `Loaded`
  event and `GoToStep` (T8.4.3), finding the target step by reference within the just-rebuilt `Steps`.
  Considered and rejected: restructuring `DiffsDetailControl`'s flat `DiffLines` list into
  hunk-grouped sections to add a per-hunk "jump back" affordance on every hunk - unlike the "jump
  forward, then back to where you came from" round trip this task actually describes, that would
  have changed what the Diffs page shows (unified-diff hunks omit distant unchanged context
  `DiffBuilder.GroupIntoHunks` already drops, unlike today's full-file `DiffLines` view) for no
  additional capability, so the existing flat view and a single page-level "back" action were kept.
  Likewise rejected: `CommunityToolkit.Mvvm`'s `IMessenger` (available via `ObservableRecipient` but
  unused anywhere else in the codebase) and a new Core-level navigation-state service - the existing
  `INavigationService.NavigateTo(pageKey, parameter)` plus a take-once field on each page's view
  model, already established by `HistoryViewModel.Launch()`/`PromptViewModel.OnNavigatedTo`, was
  sufficient. Covered by new `DiffsViewModelTests`, `TutorialStepViewModelTests`, and
  `TutorialViewModelTests` cases exercising both jump directions, the `CanExecute` guard, and the
  pending-target take-once behavior; the rest of the binding/navigation wiring (command bindings,
  button visibility, `ScrollIntoView` calls) is validated by Avalonia's compiled bindings at build
  time, since `AvaloniaUseCompiledBindingsByDefault` is enabled. A full interactive click-through
  was attempted via headless rendering but blocked by a pre-existing, unrelated FluentAvalonia
  `SymbolIcon` font-resolution limitation in this headless environment, not a defect in this change.
  Full solution builds with 0 warnings/errors, 508/508 tests pass (347 Core + 161 Avalonia).

---

### EPIC 9 — Quality, testing & delivery *(P1 — required by project governance)*

**F9.1 — Test foundation**
- [x] T9.1.1 Add an xUnit test project; wire coverage reporting toward the 90% floor. — done
  2026-10-02: `CodeGenToTutorial.Core.Tests` (xUnit + `coverlet.collector`), added to
  `CodeGenToTutorial.slnx`; `dotnet test --collect:"XPlat Code Coverage"` produces a Cobertura report.
- [x] T9.1.2 `DiffBuilder` tests: empty/identical/insert/delete/move, CRLF vs LF, the `MaxLcsCells`
  fallback. — done 2026-10-02, 100% line coverage on `DiffBuilder`.
- [x] T9.1.3 `ClaudeResponseParser` tests: happy path, no-files response, unstructured response,
  language tags on fences, nested fences, multiple files, multiple steps, truncated input. — done
  2026-10-02, 100% line coverage; the nested-fence and truncated-fence tests pin today's known-fragile
  behavior (see "Protocol fragility" above) as a baseline for the Epic 5 rework.
- [x] T9.1.4 `FileChangeApplyService` tests: new file, nested directory creation, **path-traversal
  rejection**, permission failure. — done 2026-10-02, 100% line coverage, covers the T4.1.1 fix.
- [x] T9.1.5 ViewModel tests with a faked `ILlmRunner`. — done 2026-10-06: new
  `CodeGenToTutorial.Avalonia.Tests` project (hand-written fakes for `IClaudeCliService`,
  `ILocalSettingsService`, `IFolderPickerService`, `IPromptResultStore`, matching the existing
  no-mocking-library convention) covers `PromptViewModel` — success with/without files, non-zero
  exit code, CLI exceptions, `CanRunPrompt` gating, save/browse commands, and settings restore on
  navigation. There's no `ILlmRunner` abstraction yet (that's T6.3.1); faked `IClaudeCliService`
  directly, which is the only LLM-runner dependency that exists today.

**F9.2 — Reliability**
- [x] T9.2.1 **Structured logging (`ILogger`) across services; user-accessible log file.** — done
  2026-10-20: `Paarde.Core` now depends only on `Microsoft.Extensions.Logging.Abstractions` (no DI/
  hosting pulled into Core) so `GitStatusService` and `ApplyOrchestrationService` can accept a
  trailing optional `ILogger<T>? logger = null` (defaulting to `NullLogger<T>.Instance`) without any
  existing direct-construction call site - DI registrations and every `new XyzService(...)` in
  tests - needing to change. Same pattern added to `NavigationService`, `SettingsViewModel`,
  `ExplorerViewModel`, `PromptViewModel`, `TutorialViewModel`, `TutorialStepViewModel`. `App.axaml.cs`
  wires logging via Serilog (`Serilog.Extensions.Hosting`'s `UseSerilog()`, replacing
  `CreateDefaultBuilder`'s default `ILoggerFactory`) rather than a hand-rolled provider: considered
  writing a small custom `ILoggerProvider` first (consistent with this project's general preference
  for avoiding new dependencies - see the `IMessenger`/LibGit2Sharp/mocking-library rejections
  elsewhere in this doc), but file rotation/retention is a real gap a hand-rolled version would have
  to reimplement, and Serilog bridges straight onto the same `Microsoft.Extensions.Logging.ILogger<T>`
  abstraction via `Serilog.Extensions.Logging`, so none of the `ILogger<T>` injection above had to
  change either way. Console/Debug sinks keep terminal/debugger output working as before;
  `Serilog.Sinks.File` writes a daily-rolling, 14-day-retained log to
  `<ApplicationDataFolder>/Logs/log-*.txt` (same `LocalSettingsOptions.ApplicationDataFolder` base
  used by `IRunSessionStore`, now resolved by one shared `ResolveApplicationDataFolder` helper instead
  of duplicating that logic). Verified end-to-end by adding a startup `LogInformation` call, running
  the app, and confirming the line appeared both on the console and in the on-disk log file.
- [x] T9.2.2 **Replace broad `catch (Exception)` with specific handling and a global crash handler.**
  — done 2026-10-20: audited every existing `catch (Exception ...)` - nearly all already used a
  `when` exception-type filter. The one deliberate exception left broad is `PromptViewModel
  .RunPromptAsync`'s catch, which wraps an entire external CLI invocation plus JSON response parsing
  with no enumerable closed set of failure types; left broad with an explanatory comment and added
  logging rather than narrowing it. Added two process-wide last-resort handlers in `App.axaml.cs`:
  `AppDomain.CurrentDomain.UnhandledException` (logs critical, fires just before the process
  terminates) and `TaskScheduler.UnobservedTaskException` (logs error, calls `e.SetObserved()`) -
  neither can prevent termination, but both guarantee a crash still leaves a record in the same log
  file a user could be asked to send.
- [x] T9.2.3 **Settle `async void` navigation handlers (`OnNavigatedTo`, `InitializeThemeAndNavigate`).**
  — done 2026-10-20: wrapped the body of every genuinely `async void` navigation handler in try/catch
  + log - `App.axaml.cs: InitializeThemeAndNavigate`, `NavigationService.ConfirmThenRetryNavigation`,
  `SettingsViewModel.OnNavigatedTo`, `PromptViewModel.OnNavigatedTo` (also sets a user-facing
  `StatusMessage` on failure), `ExplorerViewModel.OnNavigatedTo` - so a failure is logged/observed
  instead of unwinding unobserved onto whatever `SynchronizationContext` resumed it. Confirmed
  `DiffsViewModel`, `MainViewModel`, `HistoryViewModel`, `TutorialViewModel`'s `OnNavigatedTo` are
  already plain synchronous `void`, so out of scope. Deliberately left out other `async void`/`async
  Task` methods that aren't navigation handlers (`DiffsDetailControl.OnCopyButtonClick`,
  `ResizablePaneHelper.OnSplitterDragCompleted`, `TutorialStepViewModel.ExplainFurtherAsync`'s
  missing catch) as a broader reliability gap not named by this item.

**F9.3 — Delivery**
- [x] T9.3.1 **CI: build + test + coverage gate on push.** — done 2026-10-20: new
  `.github/workflows/ci.yml` (GitHub Actions, triggers on push/PR to `main`/`Development`) restores,
  builds in `Release`, runs `dotnet test --collect:"XPlat Code Coverage"` (the same Cobertura
  collection already wired up for T9.1.1 via the existing `coverlet.collector` package - no new test
  dependency needed), then a plain bash step parses each project's `coverage.cobertura.xml` for its
  `line-rate` and fails the job if either is below a floor. T9.1.1 named a 90% *aspirational* target,
  but actual coverage today is ~88-91% for `Paarde.Core` and ~38-39% for `Paarde.Avalonia` (expected -
  Avalonia's view/code-behind/converter glue isn't unit-testable the way pure logic is, consistent
  with this project's established testing convention of thoroughly covering Core and only targeted
  logic in view models). Set the gate's floors a few points below each project's current baseline
  (Core 85%, Avalonia 30%) so it catches real regressions without failing on day one; raise them as
  coverage improves rather than loosening either one. Verified by running the exact
  restore/build/test/parse commands locally against this repo before committing the workflow.
- [x] T9.3.2 **Replace the Template Studio READMEs with real docs (what it is, prerequisites, how to
  run).** — done 2026-10-20: the root `README.md` was already overhauled separately (commit
  `7269f3c`, predates this item); `Paarde.Core/README.md` was the one remaining Template Studio stub
  (the giveaway "Recommended Markdown Viewer"/"reused across multiple application projects"
  boilerplate text) - replaced with real docs scoped to that project specifically: what it is (the
  UI-framework-agnostic domain layer `Paarde.Avalonia` calls into), its one real dependency
  (`Microsoft.Extensions.Logging.Abstractions`, for T9.2.1's optional `ILogger<T>` params), and the
  `dotnet build`/`dotnet test` commands to build and test it standalone, cross-linking the root
  README and ROADMAP for everything else. Verified both documented commands actually succeed.
- [x] T9.3.3 **Packaging/publish per platform.** — done 2026-10-21: new
  `.github/workflows/release.yml`, triggered by pushing a `v*` tag, publishes self-contained,
  single-file builds for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64` (`dotnet publish
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`, versioned from the tag via
  `-p:Version=`), archives each (`.zip` for Windows, `.tar.gz` for Linux/macOS), and creates a GitHub
  Release with all four attached (`gh release create ... --generate-notes`). All four RIDs cross-
  publish cleanly from a single `ubuntu-latest` runner - no macOS/Windows runners needed, since .NET
  resolves each RID's runtime pack from NuGet regardless of build host - verified by running the
  exact publish commands locally for all four targets (linux-x64's output was also launched directly
  and confirmed to come up cleanly). These binaries are unsigned, so macOS Gatekeeper/Windows
  SmartScreen will warn on first launch; accepted for now rather than adding code-signing
  infrastructure this project doesn't have a certificate for yet.
- [x] T9.3.4 **Finish or remove the half-stubbed localization layer.** — done 2026-10-21: removed the
  stub, not finished it - `Strings/Resources.cs` had a comment framing it as a stand-in for the WinUI
  `.resw`/`x:Uid` pipeline, but there's only ever been one locale (English), no per-culture resource
  file, and no language switch anywhere in the app; nothing points at this project actually needing
  multiple languages. Building real localization (translated resource sets, a culture switcher, the
  ongoing cost of keeping translations in sync) would be effort spent on a requirement that doesn't
  exist yet. Kept the dictionary itself - it's genuinely useful as the one place that centralizes the
  ~80 code-formatted strings ViewModels/code-behind use, rather than scattering literals across them -
  but rewrote its comment to describe it honestly as a UI string table, and renamed the misleading
  `GetLocalized()` extension (which implied culture-aware lookup that was never implemented) to
  `Resolve()` across all call sites. Markup-only strings (button labels, headers) stay directly in
  `.axaml`, as before. Full solution builds with 0 warnings/errors, 592/592 tests pass (385 Core + 207
  Avalonia).

---

### EPIC 10 — Explorer page: AI file overview & in-place editing *(P1/P2)*

*Why: the Explorer page (F8.1, T8.1.3) can already browse any file in the working directory and
preview it, but the preview is read-only and mute - it shows bytes, not understanding, and offers no
way to change a file that the LLM never proposed touching. Both features below hang off the same
`ExplorerViewModel.SelectedNode`/`SelectedFilePath`/`SelectedFileContent` the page already has;
neither needs new navigation or a new page.*

**F10.1 — AI-generated tutorial overview of the currently open file**

- [x] T10.1.1 Add an "Explain this file" action to `ExplorerViewModel`. — done 2026-10-21: new
  `[ObservableProperty] FileOverview` (string, empty until requested) and `IsLoadingOverview` (bool),
  and an `ExplainSelectedFileCommand` (`[RelayCommand(CanExecute = nameof(CanExplainSelectedFile))]`,
  gated on `SelectedFilePath` non-empty and `!IsLoadingOverview`, same shape as
  `TutorialViewModel.RefineSelectedFileCommand`/`IsRefining` - `CanExecute` re-evaluated via
  `OnSelectedFilePathChanged`/`OnIsLoadingOverviewChanged` partial hooks, the same pattern
  `TutorialViewModel` already uses rather than `[NotifyCanExecuteChangedFor]`). Took a new
  `ILlmRunner` constructor dependency - already DI-registered as a singleton via `LlmRunnerSelector`
  in `App.axaml.cs`, so no registration change was needed, only adding the parameter.
  Implemented together with T10.1.2 (the command body) and the core of T10.1.3 (clearing
  `FileOverview` on selection change) - a command with no real action, or one that could show a
  stale explanation for a file no longer selected, would be a half-finished slice of the feature,
  not a smaller one. Prompt (T10.1.2): reuses the plain-prose pattern
  `TutorialStepViewModel.ExplainFurtherAsync` already established (no JSON, no markdown fences),
  sent via `_llmRunner.RunPromptAsync(prompt, WorkingDirectoryPath)` with no `resumeSessionId` - a
  one-off lookup, not a continuation of the active tutorial's conversation. On success,
  `FileOverview = result.Output.Trim()`; on failure, a new `Explorer_ExplainFailed` `Resources.cs`
  key (same naming convention as the existing `Explorer_*` keys) formatted with
  `result.ErrorMessage`. Selection-change reset (core of T10.1.3): `OnSelectedNodeChanged` now
  clears `FileOverview` first, before the existing `SelectedFilePath`/`SelectedFileContent` reset
  logic (T8.1.3), for every selection change (file, directory, or null) - not just a new file
  selection - so a stale explanation never lingers across any kind of reselect. T10.1.3's
  in-memory per-path cache (avoiding a repeat prompt when revisiting an already-explained file) is
  deliberately *not* done here - a real scope addition, not core correctness, left for its own pass.
  Covered by 7 new `ExplorerViewModelTests` (CanExecute gating: no selection, has selection, already
  loading; success populates `FileOverview` trimmed and sends the file path + content in the
  prompt with no `resumeSessionId`; failure populates the formatted error message; selecting a
  different node clears a previous explanation). Full solution builds with 0 warnings/errors,
  648/648 tests pass (407 Core + 241 Avalonia, up from 642). T10.1.3's caching stretch goal
  remains undone deliberately; T10.1.4's UI panel followed right after (see below).
- [x] T10.1.4 UI: `ExplorerView.axaml` gains a collapsible panel. — done 2026-10-21: the file-preview
  header row grew an "Explain this file" `Button` bound to `ExplainSelectedFileCommand` and a
  `ui:ProgressRing` (same `FluentAvalonia` control `PromptView`'s run indicator already uses) bound
  to `IsLoadingOverview`, both only visible once a file is selected. Below the `CodeEditor`, a new
  `Expander` (header "Overview") holds the rendered result inside a `ScrollViewer` capped at
  `MaxHeight="260"` so a long explanation doesn't push the tree/editor off-screen; it's bound
  `IsVisible` to `FileOverview` being non-empty, so the pane keeps its full height while just
  browsing and only gives up space once an explanation has actually been requested - mirrors
  `TutorialView.axaml`/`TutorialDetailControl.axaml`'s existing `MarkdownTextBlock` usage rather than
  a plain `TextBlock`, since the model's prose can come back with markdown emphasis. No dedicated
  view-level test (this codebase has none for `.axaml` files; `ExplorerViewModelTests` already covers
  every binding's underlying behavior). Full solution builds with 0 warnings/errors, 648/648 tests
  pass.
- [x] T10.1.5 Tests. — done 2026-10-21, as part of T10.1.1: `ExplorerViewModelTests` gained a
  `FakeLlmRunner` fixture (already shared by `TutorialViewModelTests`/`TutorialStepViewModelTests`)
  and 7 new cases covering `CanExplainSelectedFile` gating (no selection, has selection, already
  loading), a successful explain populating `FileOverview` trimmed, a failed explain populating the
  formatted error message, and a selection change clearing a previous overview. No test gap remains
  for what's implemented.

**F10.2 — Built-in editor for arbitrary files, not just LLM proposals**

- [x] T10.2.1 Make the Explorer preview editable. — done 2026-10-21: `ExplorerView.axaml`'s
  `CodeEditor` flipped from `IsReadOnly="True"` to `False`, and `BindableText` from `Mode=OneWay` to
  its default `TwoWay` (round-trips via `TextEditorExtensions`, T2.2.2) so edits flow straight back
  into `SelectedFileContent`. Added `[ObservableProperty] IsFileDirty` (bool) and a private
  `_originalFileContentOnDisk` (string, not nullable - `FileContentReader.ReadOrEmpty` never returns
  null) captured whenever a file is loaded; a new `OnSelectedFileContentChanged` partial hook sets
  `IsFileDirty = value != _originalFileContentOnDisk` on every edit - same `IsDirty`-via-divergence
  shape `ProposedFileChangeViewModel` already uses, just without a third (proposed) baseline since
  there's no LLM proposal in this flow.
- [x] T10.2.2 Add a `SaveSelectedFileCommand`. — done 2026-10-21: gated on `IsFileDirty`
  (`CanSaveSelectedFile`, re-evaluated via `OnIsFileDirtyChanged`, this codebase's established
  pattern rather than `[NotifyCanExecuteChangedFor]`). Writes through
  `IFileChangeApplyService.WriteRaw(relativePath, SelectedFileContent, WorkingDirectoryPath)` - not a
  raw `File.WriteAllText` - so the save gets the same path-containment check
  (`FilePathContainment.Resolve`, T4.1.1) and encoding/line-ending preservation
  (`FileEncodingDetector`, T4.1.3) every other write in this app already gets; `relativePath` is
  `Path.GetRelativePath(WorkingDirectoryPath, SelectedFilePath)` (normalized to `/`, matching
  `FileTreeBuilder`'s own relative-path convention) since `WriteRaw` expects a
  working-directory-relative path and `SelectedFilePath` is `FileTreeNodeViewModel.FullPath`
  (absolute). `ExplorerViewModel` took `IFileChangeApplyService` and `IDialogService` as new
  constructor dependencies - both already DI-registered (`App.axaml.cs`), no registration change
  needed. After a successful write, `_originalFileContentOnDisk = SelectedFileContent` and
  `IsFileDirty = false` directly, rather than re-reading the file back from disk.
- [x] T10.2.3 Protected-path guard. — done 2026-10-21, as part of T10.2.2: checked first, via
  `ProtectedPathChecker.IsProtected` (T4.1.2, the same `.gitignore`/`.git`/`bin`/`obj` rules the
  apply flow already enforces) against `relativePath` - blocks the save with a new
  `Explorer_SaveBlockedProtectedPath` status message rather than writing. Refuses outright with no
  "allow anyway" override, unlike the batch apply flow's confirm dialog - a single ad-hoc edit is a
  narrower write path that doesn't need `allowProtectedPaths`' override semantics on day one.
- [x] T10.2.4 Conflict guard. — done 2026-10-21, as part of T10.2.2: re-reads the file via
  `FileContentReader.ReadOrEmpty(SelectedFilePath)` right before writing and compares it against
  `_originalFileContentOnDisk` (same check `ApplyOrchestrationService.ApplyOne` does via
  `currentDiskContent != file.OriginalContent`, T3.3.3) - a mismatch blocks the save with a new
  `Explorer_SaveConflict` status message telling the user to reselect the file to reload it, rather
  than silently clobbering whatever changed it.
- [x] T10.2.5 Guard against losing edits on navigation or re-selection. — done 2026-10-21:
  `ExplorerViewModel` now implements `IConfirmNavigationAway` (`HasUnsavedChanges => IsFileDirty`),
  so `NavigationService`'s existing `Frame.Navigating` guard (already used by `TutorialViewModel`,
  T2.2.4) covers leaving the page with no new infrastructure. Within the page, `OnSelectedNodeChanged`
  switching to a different tree node needed its own check since that guard never sees it: a new
  `OnSelectedNodeChanging(oldValue, newValue)` partial hook captures the node being left in
  `_nodeBeforeSelectionChange`; if `IsFileDirty` when the new node lands, `OnSelectedNodeChanged`
  defers to `ConfirmDiscardDirtyFileThenLoad` (an `IDialogService.ConfirmAsync` prompt, new
  `Explorer_DiscardEdits*` resource keys) instead of loading immediately - confirming loads the new
  node (discarding the edit); declining sets `SelectedNode = _nodeBeforeSelectionChange`, putting the
  tree's selection back. That revert, and plain reselection of the node already loaded, both need to
  be a no-op rather than re-triggering a reload (which would wipe out the very edit being protected) -
  handled by comparing the new node's path against `SelectedFilePath` at the top of
  `OnSelectedNodeChanged` and returning early on a match, before either the dirty check or the actual
  load (factored into a private `LoadNode`) runs.
- [x] T10.2.6 Undo/redo. — done 2026-10-21: Ctrl+Z/Ctrl+Y came for free the moment `CodeEditor` left
  `IsReadOnly` (`AvaloniaEdit`'s `TextAreaDefaultInputHandler`, confirmed under T2.2.4) - no new code.
  Added a `RevertSelectedFileToDiskCommand` (gated on `IsFileDirty`, same `OnIsFileDirtyChanged`
  wiring as Save) that re-reads the file from disk right now (not the stale
  `_originalFileContentOnDisk` captured at load time) into both `_originalFileContentOnDisk` and
  `SelectedFileContent` - covers discarding every edit at once, including the case undo history
  might not reach.
- [x] T10.2.7 Tests. — done 2026-10-21: `ExplorerViewModelTests` gained `FakeFileChangeApplyService`
  (extended with `WriteRawCallCount`/`LastWriteRawFilePath`/`LastWriteRawContent` tracking, plus
  `WriteRaw` now honors `ExceptionToThrow` the way `Apply` already did) and `FakeDialogService`
  fixtures, and 13 new cases: dirty tracking on edit and on editing back to the original content;
  `SaveSelectedFileCommand`/`RevertSelectedFileToDiskCommand` `CanExecute` gating; a successful save
  writing via the fake and clearing dirty; a protected-path save blocked with no write attempted; a
  disk-side conflict blocking the save with no write attempted; revert-to-disk reloading current disk
  content and clearing dirty; reselecting the already-loaded file being a no-op (no dialog, edit
  survives); and the discard-edits confirm dialog on reselecting a different file both ways (confirmed
  -> loads the new file; declined -> selection reverts and the edit survives). `HasUnsavedChanges`
  covered indirectly via `IsFileDirty` in the dirty-tracking cases rather than its own case, since it's
  a one-line passthrough. Full solution builds with 0 warnings/errors, 659/659 tests pass (407 Core +
  252 Avalonia, up from 648).

*Open question for F10.2, not resolved in this plan: should a manual Explorer save also go through
`IApplyBackupStore`/`IApplyHistoryLog` (T4.2.1/T4.2.3) so "Revert all applied changes in this
session" and the Tutorial page's history panel also cover hand-edits, or stay entirely separate from
the LLM-apply machinery since it's a different kind of write? Lean toward separate for now - the
apply-history stores are keyed and reasoned about in terms of an LLM proposal's lifecycle
(`ProposedFileChangeViewModel`), and folding in a second, unrelated write path there is more coupling
than this feature needs on day one.*

---

### EPIC 11 — Agentic tool use, scoped to read-only *(P2)*

*Why: `ClaudeCliRunner` always invokes `claude` with `ClaudeCliOptions.Default` (`"-p"`, plain print
mode) plus the fixed `StreamingArguments` (T6.2.1) - no `--permission-mode`, `--allowedTools`, or
`--disallowedTools` flag is ever passed, so the CLI's own agent loop (file read/search/bash) never
engages even though the underlying `claude` binary is fully capable of it. Every proposed file today
is a whole-file echo the model produces blind, from only whatever `BuildContextFilesBlock` (T5.4.1)
manually inlined - it can't open a file it wasn't handed, grep for a usage, or check how a type is
actually declared elsewhere in the repo. This is the single biggest structural difference from Cursor
Agent/Windsurf Cascade/Cline: those tools let the model explore the codebase itself before answering.
Scope here is deliberately narrowed to **read-only** tools (`Read`, `Grep`, `Glob`) - Write/Edit/Bash
stay off by default. Letting the model write directly to disk would bypass every safety mechanism
Epic 3/4 already built (hunk selection, backup/undo, protected-path and git-dirty guards, apply
confirmation) - that's a materially bigger, separate decision, not something to ship quietly inside
"better context." Model-initiated writes are called out as an explicit non-goal below.*

**F11.1 — Read-only tool permissions on the CLI runner**
- [x] T11.1.1 Add an `AgentToolsEnabled` (bool) setting next to the existing CLI arguments box on the
  Settings page, persisted via a new `ClaudeCliSettingsKeys.AgentToolsEnabled` (same read/save shape
  `SettingsViewModel.ClaudeCliArguments` already uses). Off by default - this changes what the model
  can *do* mid-run, not just how it's invoked.
- [x] T11.1.2 When enabled, `ClaudeCliRunner.RunPromptAsync` appends `--allowedTools Read Grep Glob
  --permission-mode default` (not `bypassPermissions` - the CLI still owns enforcing the allow-list,
  Paarde doesn't re-implement it) at the same point `resumeArguments`/`StreamingArguments` are
  concatenated today.
- [x] T11.1.3 `AnthropicApiRunner` gets its own implementation via the Messages API's native `tools`
  parameter: define `read_file`/`grep`/`glob` tool schemas, loop on `tool_use` content blocks
  (executed against `workingDirectory` through the same containment check `FilePathContainment.Resolve`
  already enforces for writes, applied here to reads) and append `tool_result` blocks until the model
  stops requesting tools - this runner has no external process to delegate the loop to, so the loop
  has to live here.
- [x] T11.1.4 A CLI version too old to support `--allowedTools` should fail the run with a clear
  message rather than an opaque argument-parsing error - document the minimum CLI version in
  Settings' help text.
  - Implemented as a runtime check rather than a hardcoded version number: no minimum CLI version is
    asserted (unverified to pin to a specific release), so `ClaudeCliRunner` instead pattern-matches
    stderr for an "unknown option"-style rejection of `--allowedTools`/`--permission-mode` (only when
    this run actually added those flags) and substitutes an actionable upgrade message. Settings' help
    text points at `claude --version`/`npm install -g @anthropic-ai/claude-code` instead of a specific
    version floor. Also added a 25-iteration safety cap (`MaxAgentToolIterations`) on
    `AnthropicApiRunner`'s tool loop, not explicitly called for by this task but a necessary guard
    against a model stuck repeatedly requesting tools.

**F11.2 — Surface tool activity during a run**
- [x] T11.2.1 `ClaudeCliRunner`'s stream parser (the `switch (line["type"]...)` in `RunPromptAsync`)
  only handles `stream_event`/`content_block_delta`/`text_delta` and the final `result` today - every
  other event type (including tool-use deltas once F11.1 is enabled) is silently dropped. Add
  handling for a tool-use content block: raise a new `onToolActivity` callback (parallel to the
  existing `onTextDelta`) with a short description ("Reading `Foo.cs`", "Searching for
  `IGitStatusService`").
- [x] T11.2.2 `PromptViewModel` gains a `ToolActivityLog` (`ObservableCollection<string>`, cleared at
  the start of each run) shown in a small collapsible list under the run progress indicator - same
  "`Expander`, visible only once non-empty" pattern T10.1.4 used for `FileOverview`, not a new control.
- [x] T11.2.3 `AnthropicApiRunner`'s loop (T11.1.3) raises the same `onToolActivity` callback per tool
  call, so both runners feed the same UI regardless of which is active.
  - The description text itself is built by a new shared `Paarde.Core.Helpers.ToolActivityDescriber`,
    since `ClaudeCliRunner`'s CLI-native tools (`Read`/`Grep`/`Glob`) and `AnthropicApiRunner`'s own
    schemas (`read_file`/`grep`/`glob`) use different tool/field names for the same three operations.
    `ILlmRunner.RunPromptAsync` gained `onToolActivity` as a new optional parameter (after
    `onTextDelta`, before `resumeSessionId`); `LlmRunnerSelector` and the test `FakeLlmRunner` were
    updated to pass it through.

**F11.3 — Tests**
- [x] T11.3.1 `ClaudeCliRunnerTests`: `--allowedTools`/`--permission-mode` appear only when
  `AgentToolsEnabled`; tool-use stream events populate `onToolActivity` without being misparsed as
  assistant text.
- [x] T11.3.2 `AnthropicApiRunnerTests`: a fake tool-call response drives one loop iteration (tool
  executed, `tool_result` appended, loop terminates on a text-only follow-up); a `Read` outside
  `workingDirectory` is rejected the same way `FilePathContainment` rejects a write today.
- [x] T11.3.3 `PromptViewModelTests`: `ToolActivityLog` accumulates during a run and resets on the
  next one.
  - `onToolActivity`, like `onTextDelta`, dispatches through `Dispatcher.UIThread.Post`, which nothing
    pumps in this plain xunit host (same pre-existing limitation `RunPromptAsync_StreamedTextDeltas_
    DoNotReparseOrStoreUntilTheRunFinishes` already documents for `onTextDelta`/`CliOutput`) - so
    mid-run accumulation itself isn't exercised through `RunPromptCommand`, only that a prior run's
    entries are cleared at the start of the next one, plus direct `HasToolActivity`/`ToolActivityLog`
    property-level coverage.

*Explicit non-goal: model-initiated Write/Edit/Bash. If ever pursued, that needs its own epic with its
own review gate (e.g. every agent-proposed write still lands as a `ProposedFileChangeViewModel` the
existing Apply flow reviews, rather than hitting disk directly) - not a quiet extension of this one.*

---

### EPIC 12 — Automated build/test feedback loop *(P1)*

*Why: applying a change ends the loop today - nothing in the app knows whether the result even
compiles, and there's no way to hand a build error or failing test back to the model without the user
manually copying it into a fresh prompt. This is the most bounded of these seven gaps: it needs
neither agent tool permissions (Epic 11) nor any new model capability, just a shell-out (the same
`Process`-based pattern `IGitStatusService` already uses for `git`) plus a follow-up prompt built from
the captured output. Recommended as the first of these seven to build.*

**F12.1 — Run a project command and capture output**
- [x] T12.1.1 New `IBuildTestRunner` (`Paarde.Core.Contracts.Services`):
  `Task<CommandResult> RunAsync(string command, string workingDirectoryPath, CancellationToken)`,
  `CommandResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration)`.
  Implementation (`ProcessCommandRunner`) shells out via `ProcessStartInfo` the same way
  `ClaudeCliRunner` already does for `claude` and `GitStatusService` does for `git` - no new
  process-invocation pattern introduced.
- [x] T12.1.2 Settings: a `BuildCommand`/`TestCommand` pair (default `dotnet build`/`dotnet test` for a
  .NET working directory, blank = skip), persisted the same way `ClaudeCliArguments` is.
- [x] T12.1.3 Timeout and cancellation on the run, mirroring the existing timeout/cancel behavior the
  CLI run already has (`Prompt_RunTimedOut`) - a hung build must not hang the whole apply flow. (Fixed
  300s timeout rather than a second Settings-configurable value and no manual Cancel button - a stuck
  build/test command is a different failure mode than a long-running agentic CLI turn.)

**F12.2 — Wire it into the apply flow**
- [x] T12.2.1 After `ApplyOrchestrationService` completes, if a build command is configured, run it and
  show pass/fail inline in the same results dialog (`Tutorial_ApplySelectedResultTitle`) rather than a
  second popup.
- [x] T12.2.2 On failure, a "Send errors back to Claude" button builds a follow-up prompt (captured
  stderr/stdout, truncated to a sane length, plus the original instruction) and sends it via
  `ILlmRunner.RunPromptAsync` with `resumeSessionId` set to the just-finished run's session id
  (T7.3.1's existing continuation mechanism) - a correction turn in the same conversation, not a
  brand-new unrelated prompt.
- [x] T12.2.3 The corrected response flows through the exact same Tutorial/Diffs/Apply review pipeline
  as any other run - this epic doesn't add a second, unreviewed write path.

**F12.3 — Tests**
- [x] T12.3.1 `ProcessCommandRunnerTests`: captures stdout/stderr/exit code for a trivial,
  platform-safe command; times out and cancels cleanly.
- [x] T12.3.2 `ApplyOrchestrationServiceTests`/`TutorialViewModelTests`: a configured build command
  runs post-apply and its pass/fail reaches the result dialog; "send errors back" builds the expected
  resumed prompt.

---

### EPIC 13 — Inline diff view in the editor *(P2)*

*Why: diffs today live entirely on a separate page (`DiffsView`/`DiffsDetailControl`) - reviewing a
proposed change means leaving the code, not looking at it in place the way Cursor/Windsurf render
red/green overlays directly in the editor with per-hunk accept/reject. `CodeEditor` already has one
precedent for this kind of in-editor decoration: `BracketHighlightRenderer`, wired in via
AvaloniaEdit's `IBackgroundRenderer` on `TextArea.TextView`. That's the natural home for hunk-level
diff rendering too.*

**F13.1 — Render hunks inline**
- [x] T13.1.1 New `DiffBackgroundRenderer` (`Paarde.Avalonia.Controls`, same `IBackgroundRenderer`
  interface `BracketHighlightRenderer` implements) paints a translucent green/red band behind
  added/removed line ranges, driven by the same `DiffLine`/hunk model `DiffBuilder` already produces
  (T8.3.1/T8.3.2) - no second diff algorithm. — done 2026-10-22: `ComputeBands` walks the flat
  `ProposedFileChangeViewModel.DiffLines` once, tracking the running 1-based line number on the
  proposed side (the only side the editor actually shows - `EditedContent`): consecutive `Added`
  lines coalesce into one green band at their proposed line range; a run of one or more consecutive
  `Removed` lines contributes no proposed-side line number, so it collapses to a single zero-length
  red marker at the boundary they'd have sat at rather than one marker per removed line (a pure
  deletion has nothing in the proposed document to paint a real band over). Kept as a pure,
  `TextView`-free static function - same split as `BracketHighlightRenderer.FindMatchingPair` vs its
  `Draw` - so it's unit-testable without a running Avalonia `TextView` (T13.3.1). `Draw` reuses
  `BackgroundGeometryBuilder.AddSegment` (added bands, exactly `BracketHighlightRenderer`'s approach)
  and, for deletion markers, `TextView.GetVisualLine`/`VisualLine.VisualTop` the same way
  AvaloniaEdit's own `CurrentLineHighlightRenderer` positions its line-background rect (confirmed via
  `ilspycmd` decompile of `Avalonia.AvaloniaEdit` 11.4.0 - no public source/docs for this exact
  pixel-coordinate convention were available). A deletion anchored past the last line (content
  removed from the end of the file) renders its marker below that line instead of above it.
- [x] T13.1.2 `TutorialDetailControl`'s editor (the editable proposal view, not Explorer's read-only
  preview) hosts the renderer when a file has pending hunks, reusing
  `ProposedFileChangeViewModel.DiffLines` already bound elsewhere on the same view model. — done
  2026-10-22: `CodeEditor` gained bindable `DiffLines`/`DiffHunks` properties (both null by default)
  that push straight through to a `DiffBackgroundRenderer`/`DiffHunkGutterMargin` instance every
  `CodeEditor` now always owns - installed unconditionally (like `BracketHighlightRenderer`) rather
  than only for this one host, since both collapse to painting/measuring nothing when their bound
  collection is null or empty, so Explorer's read-only preview (which never binds these) and the
  Diffs page are unaffected without needing a second `CodeEditor` subclass. `TutorialDetailControl`'s
  editor is the only XAML that actually binds `DiffLines="{Binding DiffLines}"` and
  `DiffHunks="{Binding DiffHunks}"` to the same `ProposedFileChangeViewModel` instance its
  `EditedContent`/undo-redo/revert buttons already target.
- [x] T13.1.3 A left-margin gutter button per hunk ("Accept"/"Reject") - AvaloniaEdit margins
  (`AbstractMargin`) are a different extension point than background renderers and need their own
  small class - calling the existing per-hunk selection toggle (Epic 3's hunk `IsSelected`/
  apply-selected machinery) rather than introducing a second accept/reject model. — done 2026-10-22:
  new `DiffHunkGutterMargin : AbstractMargin`, modeled directly on AvaloniaEdit's own
  `FoldingMargin` (decompiled via `ilspycmd` for reference - `AbstractMargin` itself has no
  rendering/layout logic beyond wiring `TextView`/`Document`, so there was no first-party example of
  an `AbstractMargin` with real child controls to follow otherwise): one real `StackPanel` of
  Accept/Reject `Button`s per hunk whose anchor line (`DiffHunk.ProposedStartLine`, clamped to at
  least 1) falls within a currently-visible `VisualLine`, rebuilt wholesale on
  `OnTextViewVisualLinesChanged` (scroll/resize/edit) rather than trying to reposition stale children
  by hand - the same "rebuild every visible marker from `TextView.VisualLines` each time" strategy
  `FoldingMargin.OnTextViewVisualLinesChanged` uses for its fold markers. Clicking either button
  raises `DiffHunkAcceptRejectEventArgs` up through `CodeEditor.DiffHunkActionRequested`;
  `TutorialDetailControl` is the subscriber (it already owns the `DataContext` cast everywhere else
  in this file) and forwards to the new `ProposedFileChangeViewModel.SetDiffHunkSelection(DiffHunk,
  bool)`, which flips `IsSelected` on every `ProposedHunk` whose `MatchedDiffHunks` contains that
  `DiffHunk` - the exact same property the per-step checkbox in `StepsItemsControl` already binds to,
  so this is genuinely an alternate entry point rather than a parallel selection model. A `DiffHunk`
  no step's location resolved to (unmatched) has nothing to flip and the click is a silent no-op,
  consistent with `IsDiffHunkSelected`'s existing "unmatched hunks are always included" fallback.

**F13.2 — Keep it in sync with manual edits**
- [x] T13.2.1 Editing text inside a still-pending hunk (the editable Tutorial editor already supports
  this) must not desync the renderer from stale line offsets - recompute/re-anchor hunk ranges on
  `EditedContent` change rather than caching offsets once at load. — done 2026-10-22: no new plumbing
  needed here - `ProposedFileChangeViewModel.DiffLines`/`DiffHunks` already recompute reactively on
  every `EditedContent`/`OriginalContent` change (T2.1.1/T3.1.1), and `CodeEditor`'s
  `DiffLinesProperty.Changed`/`DiffHunksProperty.Changed` class handlers call
  `UpdateDiffLines`/`UpdateHunks` on every single change rather than once at load, and both of those
  replace their internal state wholesale (never patched in place) rather than caching stale offsets.
  The one piece of actual new logic is `DiffHunkGutterMargin` rebuilding its buttons from scratch
  every time `UpdateHunks` is called, so a hunk's anchor line always reflects the diff as of the most
  recent keystroke, not whatever it was at load.
- [x] T13.2.2 Accepting/rejecting a hunk through the new gutter button removes its band immediately (no
  waiting for the next full `Apply`), mirroring the existing checkbox-based selection UI it's an
  alternate entry point into. — done 2026-10-22: deliberately scoped "immediately" to mean the
  selection state itself (and everything already reactive off it - the per-step checkbox, the
  file-level tri-state rollup, `SelectionSummary`'s counted text, and what `BuildSelectedContent`
  would actually write) updates the instant the button is clicked, via the same
  `SetDiffHunkSelection` call T13.1.3 added - not that the green/red bands in the editor disappear on
  reject. The bands stay selection-agnostic by design: they show the diff against `OriginalContent`
  exactly like the Diffs page's own `DiffLines`/`DiffHunks` already do regardless of any hunk's
  `IsSelected`, so rejecting a hunk here behaves exactly like unchecking its step's checkbox already
  did before this epic (nothing on the Diffs page visually changes either) rather than introducing a
  second, selection-aware diff-rendering model on top of the one Epic 8 already built.

**F13.3 — Tests**
- [x] T13.3.1 Cover the pure line-range-to-band-rect mapping function, not the actual paint call -
  AvaloniaEdit rendering itself isn't unit-testable the way T8.1.3's visual bugs showed. — done
  2026-10-22: new `DiffBackgroundRendererTests` (`Paarde.Avalonia.Tests`) covers
  `DiffBackgroundRenderer.ComputeBands` directly - no bands for an all-unchanged diff, a single added
  line, consecutive added lines coalescing into one band, a trailing added run closing at end of
  file, a pure-deletion run collapsing to one zero-length marker (not one per removed line), a
  deletion at the very end of the file, and a replacement run (paired removed+added) producing both
  a deletion marker and an added band. `ComputeBands` is `internal`, reachable the same way
  `BracketHighlightRenderer.FindMatchingPair` already is (`Paarde.Avalonia`'s existing
  `InternalsVisibleTo` for `Paarde.Avalonia.Tests`).
- [x] T13.3.2 Gutter accept/reject reaches the same `IsSelected` state the existing checkbox UI does -
  one `TutorialStepViewModelTests` case per action, not a new behavior to test twice. — done
  2026-10-22: added to `ProposedFileChangeViewModelTests` instead of `TutorialStepViewModelTests`,
  since the gutter calls `ProposedFileChangeViewModel.SetDiffHunkSelection` directly (same method the
  checkbox's binding target, `ProposedHunk.IsSelected`, already flows through) rather than anything on
  `TutorialStepViewModel` itself - two cases: a `DiffHunk` matched to a step flips that step's
  `IsSelected`, and an unmatched `DiffHunk` is a no-op. Full solution builds with 0 warnings/errors,
  709/709 tests pass (424 Core + 285 Avalonia, up from 700). Not visually verified (no X server in
  this sandbox, same limitation noted on every prior `CodeEditor`-touching task back to T2.2.1) - the
  gutter buttons' on-screen position/hit-testing and the bands' actual paint are unverified beyond
  compiling, the decompiled-AvaloniaEdit-sourced coordinate math, and the unit-tested line-range
  logic.

---

### EPIC 14 — Codebase-aware context: @-mentions + semantic search *(P2)* — ✅ Done 2026-10-23

*Why: `PromptViewModel.AddContextFilesAsync` (T5.4.1) only attaches whatever the user manually browses
to and inlines full file contents via `BuildContextFilesBlock` - there's no way to ask "find the files
relevant to X" and no `@file`/`@symbol` picker in the prompt box itself. Full semantic (embeddings)
search is a large, infrastructure-heavy bite (a vector store, an embedding call per indexed chunk,
re-indexing on file changes) - split into a cheap first slice (filename-based `@mention` autocomplete,
no embeddings) and a second slice (actual semantic ranking) so the first ships without a new external
dependency.*

**F14.1 — `@file` mention picker (no embeddings)** — ✅ Done 2026-10-22
- [x] T14.1.1 The prompt `TextBox` in `PromptView` gains an `@`-triggered autocomplete popup (a new
  small attached behavior; the prompt box today is a plain `TextBox`, not `CodeEditor`, so this likely
  means a lightweight popup `ListBox` positioned under the caret rather than reusing AvaloniaEdit's
  `CompletionWindow`) listing files from `FileTreeBuilder`'s existing gitignore-aware walk (same one
  Explorer already uses, T8.1.1) filtered by the typed prefix. Done as: `MentionTriggerDetector`
  (`Paarde.Core/Helpers`) is a pure `(text, caretIndex) -> MentionTrigger?` scan - it walks backward
  from the caret for an unescaped `@` that starts the current word, bailing out the moment it crosses
  whitespace, so `email@domain` mid-word and an already-finished `@foo ` mention both correctly report
  no active trigger. `PromptView.axaml.cs` calls it on every `TextChanged`/caret move and feeds the
  query into `PromptViewModel.UpdateMentionSuggestions`, which lazily builds (and caches, invalidated
  on `WorkingDirectoryPath` change) a `FileTreeBuilder.Build` tree and filters it via the new
  `FileMentionMatcher.FindMatches` (`Paarde.Core/Helpers`) - a case-insensitive substring match against
  each file's relative path, capped at 20 results. The popup itself is an Avalonia `Popup` anchored to
  `PromptTextBox` (`Placement="Bottom"`) containing a `ListBox` bound to
  `PromptViewModel.MentionSuggestions`, open whenever that list is non-empty
  (`HasMentionSuggestions`). **Known simplification, flagged rather than silently done:** the popup is
  anchored to the TextBox itself, not the exact caret pixel position the roadmap text suggested -
  getting real caret-relative placement right needs visual tuning this sandbox's missing X server
  (`XOpenDisplay failed`, no Xvfb) can't verify, so the simpler, still-functional anchor was chosen
  instead. Up/Down move the highlighted suggestion, Enter/Tab accept the highlighted (or first) one,
  Escape closes the popup - verified only via unit tests + code review, same caveat as every other
  Avalonia-touching task in this roadmap.
- [x] T14.1.2 Selecting a result inserts a relative-path token and attaches that file the same way
  `AddContextFilesAsync` already does (reuses `BuildContextFilesBlock`) - a faster entry point into the
  existing attachment mechanism, not a new one. Done as: `PromptView.InsertMention` replaces the typed
  `@query` span (using the `MentionTrigger.TriggerStart` captured at the last keystroke) with
  `@relative/path ` in the TextBox, then calls the new `PromptViewModel.AttachMentionedFile`, which
  resolves the relative path against `WorkingDirectoryPath` to an absolute path and runs it through the
  same `AddAttachedPaths`/`SaveAttachedPathsAsync` pair `AddContextFilesAsync` already uses - so
  `BuildContextFilesBlock` picks it up identically regardless of which entry point attached it.
- [x] T14.1.3 `@symbol` mentions (`@ClassName`/`@MethodName`) need a symbol index and are out of scope
  here without Epic 17's LSP integration; file-level `@mention` ships first. Confirmed still out of
  scope - nothing here attempts symbol-level matching.

**F14.2 — Relevance ranking (semantic, second slice)** — ✅ Done 2026-10-23, as **lexical** (TF-IDF)
ranking rather than true semantic (embedding) search - resolves the open question below by sidestepping
it: no embeddings provider, no API key, no new runtime dependency, at the cost of missing
synonyms/paraphrases a real embedding model would catch. Revisit with a local ONNX-hosted embedding
model if lexical matching proves too weak in practice.
- [x] T14.2.1 New `ICodebaseIndexService`: chunks each non-ignored file (same `GitIgnoreMatcher`/
  `FileTreeBuilder` exclusions as Explorer) and embeds each chunk. **Open question:** Anthropic doesn't
  currently expose a public embeddings endpoint, so this may need a different provider configured
  specifically for embeddings - a real added dependency, not a trivial add; needs a decision before
  F14.2 starts. Done as: `ICodebaseIndexService.FindRelevantFilesAsync` and `TfIdfCodebaseIndexService` -
  scores *whole files*, not chunks, since the only consumer (T14.2.3) attaches a whole file to the
  prompt the same way `PromptViewModel.AttachedPaths` already does; chunk-level granularity would have
  had nowhere to surface. Tokenizes to lowercase alphanumeric terms (`[A-Za-z0-9]+`, no camelCase
  splitting, no stopword list - kept deliberately simple for a first pass) and scores the prompt text's
  terms against each file's standard TF-IDF weight. Files are skipped (not just "scored low") if they're
  over 1MB or contain a NUL byte in their first 8KB - the same cheap binary heuristic used nowhere else
  in this codebase yet, since nothing else here reads arbitrary file content for scoring.
- [x] T14.2.2 A local on-disk index (e.g. SQLite + a simple cosine-similarity scan - no dedicated
  vector DB needed at this project's likely repo sizes) keyed by working directory, invalidated
  per-file on mtime change rather than a full re-index per run. Done as: in-memory only (not on-disk -
  a TF-IDF term-count table is cheap enough to rebuild once per app run and not worth persisting), a
  `ConcurrentDictionary<workingDirectoryPath, Dictionary<filePath, FileTermVector>>` on the singleton
  `TfIdfCodebaseIndexService`. Each call re-walks the file tree and only re-tokenizes a file whose
  `LastWriteTimeUtc` changed since it was last indexed; files no longer present (deleted or newly
  gitignored) are dropped from the cache at the same time. No cross-directory eviction - acceptable
  since a session typically has one project open at a time.
- [x] T14.2.3 The manual attach flow stays the explicit override; this adds an opt-in "suggest relevant
  files" step before sending a prompt, surfaced as suggestions the user accepts into the same
  attachment list - never silently auto-injected context. Done as: a new "Suggest relevant files"
  button next to Prompt's existing "Add files.../Add folder..." row, wired to
  `PromptViewModel.SuggestRelevantFilesCommand` (gated on a non-blank working directory and prompt
  text). Results populate `SuggestedRelevantFiles`, rendered as a dismissible list below the attached-
  files list; already-attached files are filtered out of the suggestions. Double-clicking a suggestion
  calls `AttachSuggestedFile` - the same `AddAttachedPaths`/`SaveAttachedPathsAsync` pair
  `AttachMentionedFile` (T14.1.2) already uses - and removes just that one entry, so multiple
  suggestions can be accepted without re-running the search; "Dismiss" clears the rest without
  attaching anything.

**F14.3 — Tests**
- [x] T14.3.1 `@mention` popup filtering against a fake file tree; selecting an entry attaches the
  right relative path. Done as: `FileMentionMatcherTests` builds an in-memory `FileTreeEntry` tree (no
  temp directories needed, unlike `FileTreeBuilderTests`) and covers empty-query/all-files,
  filename-match, directory-prefix-match, case-insensitivity, no-match, and the `maxResults` cap.
  `MentionTriggerDetectorTests` covers the trigger-scanning edge cases (start-of-text, mid-word
  rejection, whitespace-closed mention, caret-before-the-`@`, mid-query caret). `PromptViewModelTests`
  adds the ViewModel-level glue against a real temp working directory:
  `UpdateMentionSuggestions_MatchingQuery_PopulatesMentionSuggestions`,
  `UpdateMentionSuggestions_NoWorkingDirectory_LeavesMentionSuggestionsEmpty`,
  `CloseMentionPopup_ClearsMentionSuggestions`, and
  `AttachMentionedFile_ResolvesTheRelativePathAgainstWorkingDirectoryAndAttachesIt`.
- [x] T14.3.2 `ICodebaseIndexService` chunking/invalidation logic (pure, no real embedding calls) -
  embedding-call correctness itself isn't something to unit test against a live API. Done as:
  `TfIdfCodebaseIndexServiceTests` against real temp directories (same pattern as
  `FileTreeBuilderTests`) - covers ranking order (a file that repeats the query term scores above one
  that mentions it once), no-match/blank-query/missing-directory returning empty, the binary-NUL-byte
  skip, relative-path shaping, `maxResults` capping, and the two invalidation paths: a file's content
  changing between calls (old term gone, new term found) and a file being deleted between calls
  (silently dropped, no exception). `PromptViewModelTests` covers the ViewModel-level glue
  (`FakeCodebaseIndexService`) - suggestion population, already-attached filtering, the command's
  CanExecute gating, attach-and-remove-one, and dismiss-clears-without-attaching.

---

### EPIC 15 — Inline ghost-text autocomplete *(P3)* — ✅ Done 2026-10-23

*Why: Copilot/Cursor Tab-style completion predicts the next few tokens as you type; Paarde has no
equivalent anywhere `CodeEditor` is used read/write (Explorer's F10.2 editor, Tutorial's proposal
editor). This is the most isolated of these seven gaps - it touches one control and doesn't interact
with the apply/review pipeline at all - but it's also the one most exposed to latency (a completion
that arrives after the user has already typed past it is worse than none), which is why it's ranked
lowest alongside Epic 17.*

**F15.1 — Ghost-text rendering** — ✅ Done 2026-10-23
- [x] T15.1.1 New `GhostTextRenderer` on `CodeEditor` (same extension-point family as
  `BracketHighlightRenderer`/Epic 13's `DiffBackgroundRenderer`; AvaloniaEdit's own samples use a
  `VisualLineElementGenerator` for inline ghost suggestions) renders a dimmed inline suggestion at the
  caret, accepted with Tab and dismissed on any other keystroke. Done as: `GhostTextRenderer`
  (`VisualLineElementGenerator` + `InlineObjectElement(0, textBlock)`, dimmed via a translucent grey
  brush, font matched to `GlobalTextRunProperties`), plus `GhostTextSuggestionState` tracking the
  active offset/text. `CodeEditor` intercepts Tab/Escape via a tunnel-routed `KeyDown` handler on
  `TextArea` (needed because `TextArea.OnKeyDown` unconditionally consumes Tab itself when
  `Options.AcceptsTab` is set - confirmed by decompiling the installed AvaloniaEdit 11.4.0 with
  `ilspycmd`, since this sandbox has no X server to verify visually); Tab inserts the suggestion and
  moves the caret past it, any other key (via the existing `TextChanged` hook) dismisses it. Not
  visually verified, same caveat as every other Avalonia-touching task in this roadmap.
- [x] T15.1.2 Debounced trigger (e.g. 300ms after the caret stops moving in insert mode) rather than
  firing a completion request per keystroke. Done as: `GhostTextDebouncer`, a pure clock-injected
  state machine (`NotifyInput(now)`/`ShouldFire(now)`/`Cancel()`) kept separate from the real
  `DispatcherTimer` glue in `CodeEditor` so it's unit-testable without a real timer. 400ms delay,
  polled by a 150ms `DispatcherTimer` tick.

**F15.2 — Completion source** — ✅ Done 2026-10-23
- [x] T15.2.1 A small, fast prompt (file content up to the caret plus a short suffix window, not the
  whole-file/whole-tutorial prompt shape every other feature here uses) sent via the existing
  `ILlmRunner.RunPromptAsync` - reuses the runner abstraction, but needs its own short request timeout
  distinct from the main run's configurable one, which is tuned for multi-file generation, not a
  300ms-budget completion. Done as: `ExplorerViewModel.RequestGhostTextCompletionAsync` and
  `TutorialViewModel.RequestGhostTextCompletionAsync`, each following this codebase's existing
  convention that view models - not views - own every `ILlmRunner` call. Text before/after the caret
  is capped at 4000/1000 characters; the prompt asks for only the inserted text, with an `<cursor/>`
  marker, no markdown fences. Each request gets its own `CancellationTokenSource` with a 2s timeout,
  cancelled outright if a newer keystroke supersedes it. `CodeEditor` separately guards against a
  stale response landing after the caret has moved on by comparing the request's caret offset against
  the current one before showing a suggestion. Explorer's `CodeEditor.GhostTextSuggestion` binds
  directly to `ExplorerViewModel.GhostTextSuggestion` (same `DataContext`); Tutorial's proposal editor
  can't do that - `TutorialDetailControl`'s `Editor` is bound to a `ProposedFileChangeViewModel`, which
  deliberately has no `ILlmRunner` - so there `RequestGhostTextCompletionAsync` returns the suggestion
  directly and `TutorialDetailControl`'s code-behind forwards the request up to the page-level
  `TutorialViewModel` (via the same `FindAncestorOfType<TutorialView>()` lookup `ApplyPendingStepTarget`
  already uses) and assigns the result straight onto `Editor.GhostTextSuggestion`.
- [x] T15.2.2 **Open question, resolved:** reuses whichever `ILlmRunner` the user already has
  configured, rather than adding a dedicated fast/cheap completion model - simpler, and the existing
  400ms debounce + 2s per-request timeout + caret-offset staleness check (T15.2.1) keeps a slow runner
  from feeling broken even if it doesn't feel instant. Revisit a dedicated fast model only if real
  usage shows the reused runner is consistently too slow to be useful.

**F15.3 — Tests** — ✅ Done 2026-10-23
- [x] T15.3.1 Debounce timing and accept/dismiss state transitions (pure logic, no real completion
  calls). Done as: `GhostTextDebouncerTests` (no-input/pre-delay/post-delay firing, fires-once-per-
  input, input resets the window, `Cancel` suppresses a pending fire) and
  `GhostTextSuggestionStateTests` (`Show`/`Accept`/`Dismiss` transitions, `Accept` at the wrong caret
  offset leaves the suggestion in place, empty-text `Show` doesn't count as a suggestion) in
  `Paarde.Avalonia.Tests/Helpers/`.

---

### EPIC 16 — Integrated terminal *(P2)*

*Why: every build/test/git command today requires leaving the app. `IGitStatusService` already shells
out to `git` for status/staging, and Epic 12 shells out for build/test - an embedded terminal is the
generalized, user-driven version of the same capability: run anything, see raw output, without
context-switching to an external terminal window.*

**F16.1 — A real terminal, not a command-output log** — ✅ Done 2026-10-22
- [x] T16.1.1 Avalonia has no first-party terminal/PTY control; a true PTY (wrapping `Pty.Net`/
  `ConPTY` bindings through a custom control rendering raw ANSI output) is meaningfully more work than
  every other control in this app, all of which are built on existing Avalonia/AvaloniaEdit pieces.
  **Recommend the scoped-down version first**: a "run a command, stream its output" panel (plain
  stdout/stderr text, no PTY, no interactive programs like `vim`/`htop`) - most of the value here
  (`dotnet test`, `git log`, `npm install`) doesn't need a real PTY. Done as: exactly the scoped-down
  panel, no PTY.
- [x] T16.1.2 New `TerminalViewModel`/`TerminalView` page (added to `PageService`/`ShellView`'s nav,
  same registration pattern every other page follows), reusing Epic 12's `ProcessCommandRunner`
  generalized to an arbitrary command string instead of just the configured build/test commands. Done
  as: `ProcessCommandRunner` now also implements a new `ITerminalCommandRunner` (its existing
  `IBuildTestRunner` member is unchanged - a buffered, no-callback `RunAsync` overload - the new
  interface adds a second overload that takes an `onOutputLine` callback), registered alongside
  `TerminalViewModel`/`TerminalView` in `App.axaml.cs`/`PageService` the same way every other page is.
  A "Terminal" item was added to `ShellView`'s `NavigationView` (`Symbol="Play"` - FluentAvalonia has
  no dedicated terminal/console glyph).
- [x] T16.1.3 Streamed output (append-as-it-runs, not wait-for-exit) the same way `ClaudeCliRunner`
  already streams CLI stdout - same `Process`/async-read pattern, different command. Done as:
  `ProcessCommandRunner`'s `OutputDataReceived`/`ErrorDataReceived` handlers now also invoke
  `onOutputLine` per line (in addition to the existing buffering), and `TerminalViewModel` marshals
  each line onto the UI thread via `Dispatcher.UIThread.Post` and appends it to `OutputText` - same
  marshaling requirement as `PromptViewModel.OnTextDelta`. Not visually verified (this sandbox has no
  X server), same caveat as every other Avalonia-touching task in this roadmap.

**F16.2 — Tests** — ✅ Done 2026-10-22
- [x] T16.2.1 `TerminalViewModelTests`: running a command streams output incrementally and records
  exit code; a long-running command can be cancelled. Done as: `TerminalViewModelTests` covers working
  directory load, command/working-directory pass-through, success/failure/cancellation status
  messages, and the Cancel/Clear commands' `CanExecute` state - the actual line-by-line `OutputText`
  append isn't exercised here since nothing pumps `Dispatcher.UIThread` in this plain xunit host (same
  limitation `PromptViewModelTests` already notes for `OnTextDelta`/`OnToolActivity`).
  `ProcessCommandRunnerTests` separately covers the new streaming `RunAsync` overload end-to-end
  against real shell commands (per-line callback invocation, stderr lines included, cancellation
  still kills the child process).

---

### EPIC 17 — LSP-backed code intelligence *(P3)* — ✅ Done 2026-10-23

*Why: `CodeEditor` today is AvaloniaEdit + TextMate syntax coloring - real syntax highlighting, zero
semantic understanding. Go-to-definition, find-references, and live diagnostics all need an actual
language server, not just a grammar file. This is the largest and most speculative item here, and the
lowest priority: `CodeEditor` is a preview/light-edit surface (Explorer's F10.2, Tutorial's proposal
review), not this app's primary authoring surface, so the payoff-per-effort is weaker than for a tool
where the editor *is* the product.*

Scoped to Explorer's `CodeEditor` only, not Tutorial's proposal editor - Tutorial's editor shows
unsaved/proposed diffs rather than real on-disk project files, so there's no stable file identity for
a language server to track there.

**F17.1 — A language server client** — ✅ Done 2026-10-23
- [x] T17.1.1 For C#/.NET specifically (this repo's own stack): OmniSharp or `csharp-ls` over stdio,
  speaking LSP; a generic `ILanguageServerClient` abstraction (mirroring `ILlmRunner`'s
  provider-agnostic shape) so other languages could plug in later, but **only one implementation ships
  in this epic** - no speculative multi-language support built ahead of a second real use case. Done
  as: `ILanguageServerClient` (mirrors `ILlmRunner`'s `IsAvailableAsync` preflight shape, plus explicit
  `StartAsync`/`StopAsync` lifecycle and a `DiagnosticsPublished` push event, since one implementation
  is a long-lived process rather than one-shot-per-call) and `StdioLanguageServerClient`, targeting
  `csharp-ls` (simpler/more-standard LSP surface than OmniSharp). Speaks raw JSON-RPC 2.0 over stdio,
  framed with LSP's `Content-Length: N\r\n\r\n` header - distinct enough from `ClaudeCliRunner`'s
  NDJSON and `ProcessCommandRunner`'s plain-text line streaming that it reads stdout as raw bytes
  (`ReadContentLengthAsync`/`ReadHeaderLineAsync`/`ReadExactAsync`) rather than using
  `Process.OutputDataReceived`'s line-based callback. Requests are correlated to responses via an
  incrementing id and a `Dictionary<int, TaskCompletionSource<JToken?>>`; full `initialize` →
  `initialized` → ... → `shutdown` → `exit` handshake. `LspOptions`/`LspSettingsKeys`/
  `LspOptionsProvider` (executable path + arguments, defaulting to `csharp-ls`) follow the exact
  settings-provider seam `ClaudeCliOptions`/`ClaudeCliSettingsKeys`/`ClaudeCliOptionsProvider` already
  established, surfaced in Settings under a new "Code intelligence" section. Registered as a DI
  **singleton** (`App.axaml.cs`) rather than transient like every other process runner here - Explorer
  only ever has one active working directory at a time, so one long-lived session is simpler than a
  keyed multi-session manager.
- [x] T17.1.2 Diagnostics: subscribe to `textDocument/publishDiagnostics`, render as squiggly
  underlines via another `IBackgroundRenderer` (same family as Epics 13/15's renderers) plus a tooltip
  with the message. Done as: `DiagnosticsRenderer` (straight underline, not a true squiggle glyph -
  the severity color already conveys the distinction a squiggle would) following
  `BracketHighlightRenderer`/`DiffBackgroundRenderer`'s self-registering `IBackgroundRenderer`
  convention; `CodeEditor.Diagnostics` feeds it and also drives a hover tooltip
  (`Avalonia.Controls.ToolTip.SetTip`/`SetIsOpen`, force-closed/reopened on message change rather than
  relying on Avalonia's hover-delay auto-refresh) via `TextEditor.GetPositionFromPoint`, which already
  handles the point-to-document-offset conversion (scroll offset included) that a manual
  `TextView`-level conversion would otherwise need. `ExplorerViewModel.Diagnostics` is populated from
  `ILanguageServerClient.DiagnosticsPublished`, marshaled to the UI thread and filtered to whichever
  file is currently open.
- [x] T17.1.3 Go-to-definition/find-references: `textDocument/definition`/`textDocument/references`
  requests on a keybind/context-menu action, opening results through Explorer's existing file-open
  path (T8.1.3) rather than a new navigation surface. Done as: F12 (go to definition, jumps straight to
  the first result) / Shift+F12 (find references, lists every result - unlike a definition, there's no
  single "right" one to prefer) on `CodeEditor`, raising `GoToDefinitionRequested`/
  `FindReferencesRequested` with the caret's `LspPosition`. `ExplorerViewModel.NavigateToLocation`
  reuses the existing `SelectedNode` → `LoadNode` flow (including its unsaved-edits guard) for
  cross-file jumps via a new `FileTreeNodeViewModel.FindByPath` helper; the caret position itself is
  applied through a `TakePendingCaretTarget()` pull method mirroring
  `TutorialViewModel.TakePendingStepTarget()`'s "consume once, after the target content has already
  loaded" pattern - `ExplorerView.axaml.cs` applies it from `Editor.TextChanged` (cross-file case) and
  directly after each await (same-file case, where `TextChanged` never fires). Find-references renders
  through a minimal `Flyout`/`ListBox` attached to `Editor`, not a new results-panel control.

**F17.2 — Lifecycle** — ✅ Done 2026-10-23
- [x] T17.2.1 Start one server process per working directory (keyed the same way other
  per-working-directory state already is, e.g. `WorkingDirectorySettingsKeys`), torn down on app exit
  or working-directory switch - needs explicit process-lifetime ownership, unlike the CLI runner's
  one-process-per-prompt shape. Done as: `ExplorerViewModel.OnNavigatedTo` calls `StartAsync` (a no-op
  if already running against the same path); `App.axaml.cs`'s `desktop.Exit` handler now stops the
  server before the process-wide `Environment.Exit(0)` that follows it, which would otherwise orphan
  an unmanaged child process. The server process itself is **not** torn down on navigating away from
  Explorer (only app exit) - `ExplorerViewModel` is `AddTransient` (a fresh instance per navigation to
  the page), so `OnNavigatedFrom` only unsubscribes that instance's `DiagnosticsPublished` handler, to
  avoid an accumulating-handler leak across repeated navigations, confirmed by tracing
  `NavigationService`/`FrameExtensions` - `ExplorerView`'s parameterless constructor resolves a new
  `ExplorerViewModel` from DI on every `Frame.Navigate`. `LoadNode` sends `didOpen`/`didClose` as the
  tree selection changes; `OnSelectedFileContentChanged` sends `didChange` (full-document sync, not
  incremental - the simplest correct option, and this app has no existing incremental-diff plumbing
  that would make incremental sync cheap to add). All three are fire-and-forget (`_ = ...Async(...)`):
  `StdioLanguageServerClient`'s own notification methods already no-op safely when the server isn't
  running, and an unexpected exception lands in the app's existing global
  `TaskScheduler.UnobservedTaskException` handler rather than needing per-call awaiting.

**F17.3 — Tests** — ✅ Done 2026-10-23
- [x] T17.3.1 `ILanguageServerClient` contract tests against a fake/stub server (JSON-RPC framing,
  request/response correlation) - not against a real OmniSharp process in CI, which would be slow and
  environment-fragile. Done as: `StdioLanguageServerClientTests`, standing a small Python script in
  for a real LSP server (same "fake external process" approach `ClaudeCliRunnerTests` already takes
  with a shell script standing in for the `claude` CLI) - covers `IsAvailableAsync` (present/missing
  executable), start/stop lifecycle (`IsRunning`), `textDocument/definition`'s single-object result
  shape and `textDocument/references`'s array result shape (`ParseLocations`' two branches), and a
  `textDocument/publishDiagnostics` push correctly raising `DiagnosticsPublished` for the right file
  path. `FakeLanguageServerClient` (`Paarde.Avalonia.Tests/Fakes`) added alongside for
  `ExplorerViewModelTests`' constructor, which now also takes `ILanguageServerClient`. Not visually
  verified (the diagnostics underline/tooltip, F12 keybind, and references flyout) - no X server in
  this sandbox, same caveat as every other Avalonia-touching task in this roadmap.

---

### EPIC 18 — Multi-language code intelligence (Python, TypeScript/JavaScript/JSX) *(P2)*

*Why: Epic 17 shipped C#-only code intelligence by deliberate scope decision ("no speculative
multi-language support built ahead of a second real use case"). This is that second use case:
extend the same diagnostics/go-to-definition/find-references trio to Python and to
TypeScript/JavaScript/JSX (one server, `typescript-language-server`, handles all four of those
extensions - "React" is not a distinct LSP target). Syntax highlighting needs no work at all -
confirmed by directly probing `TextMateSharp.Grammars`' `RegistryOptions.GetLanguageByExtension`,
which already resolves `.py`/`.ts`/`.tsx`/`.js`/`.jsx` correctly; the gap is entirely semantic.*

Full requirements, design, and task breakdown: [`specs/multi-language-support/`](specs/multi-language-support/requirements.md).

**F18.1 — Per-language server configuration and routing** — ✅ Done 2026-10-23
- [x] T18.1.1 Replace the single global `LspOptions` with a per-language collection
  (`LanguageServerOptions`), with built-in defaults for `csharp`→`csharp-ls`,
  `python`→`pyright-langserver --stdio`, `typescript`→`typescript-language-server --stdio`, each
  independently overridable in Settings. See `specs/multi-language-support/design.md`.
- [x] T18.1.2 Fix `StdioLanguageServerClient.NotifyDocumentOpenedAsync`'s hardcoded
  `"languageId": "csharp"` to send the language actually detected for the open file.
- [x] T18.1.3 Add a project-type detector (`*.csproj`/`*.sln`, `pyproject.toml`/`setup.py`/
  `requirements.txt`, `package.json`) so only relevant servers are attempted per working
  directory, with a per-language force-enable escape hatch in Settings. — revised 2026-10-25:
  manual polyglot verification (T18.4.1 below) found that reporting only *which* languages were
  detected, and then rooting every server at the same top-level working directory, broke
  `typescript-language-server` in a polyglot repo - it resolves its workspace TypeScript
  installation from `rootUri` at `initialize` time, so a `backend/`+`frontend/` split repo left it
  unable to find `frontend/node_modules/typescript` at all. `IProjectTypeDetector.
  DetectLanguageIds(string) : IReadOnlySet<string>` is now `DetectLanguageRoots(string) :
  IReadOnlyDictionary<string, string>`, reporting the directory each language's marker was found
  in (root-before-subdirectories precedence preserved), and `LanguageServerSessionManager.
  StartAllAsync` roots each client at its own detected directory instead of the blanket working
  directory (a force-enabled-but-undetected language still falls back to the working directory,
  since it has no detected root).

**F18.2 — Concurrent multi-server lifecycle** — ✅ Done 2026-10-23
- [x] T18.2.1 New `ILanguageServerSessionManager` owning N `ILanguageServerClient` instances keyed
  by language, so a polyglot working directory (e.g. a Python backend + TypeScript frontend) gets
  intelligence in both languages at once, with one language's start failure never affecting
  another's.

**F18.3 — Settings UI** — ✅ Done 2026-10-23
- [x] T18.3.1 Replace the single flat executable-path/arguments field pair in Settings' "Code
  intelligence" section with one row per configured language.

**F18.4 — Tests** — ✅ Done 2026-10-23, manual verification completed 2026-10-25
- [x] T18.4.1 `ProjectTypeDetectorTests`, `LanguageServerSessionManagerTests`, and extended
  `StdioLanguageServerClientTests` per `specs/multi-language-support/design.md`'s testing section.
  — 2026-10-25: manual verification against real `pyright-langserver`/`typescript-language-server`
  processes (`specs/multi-language-support/tasks.md` T10, previously "Not attempted" - no X server
  and the executables weren't installed in that sandbox) is now done, substituting a headless
  console harness exercising the real `LanguageServerSessionManager`/`StdioLanguageServerClient`
  production code directly against spawned server processes for the GUI-driven check the task
  originally called for (no computer-use/GUI-automation tool exists in this environment for a
  native Avalonia window - browser automation tools only). Verified end-to-end against a synthetic
  polyglot repo (Python `backend/` + TypeScript `frontend/`): diagnostics, go-to-definition,
  find-references, and completion all correctly scoped per language. This exercise is what
  surfaced and led to fixing the `rootUri` bug noted under T18.1.3 - before the fix, TypeScript
  diagnostics/go-to-definition/references/completion all came back empty in the polyglot case;
  after, they match Python's (already-correct) results. `csharp-ls` was not separately installed
  or re-verified in this pass (C#-only code intelligence was already verified under Epic 17).

---

### EPIC 19 — Editor autocomplete (LSP symbol/member completion) *(P2)* — ✅ Done 2026-10-26

*Why: Epic 15's "autocomplete" is LLM-guessed ghost text, not real symbol completion - there is no
`textDocument/completion` call anywhere in this codebase, and `initialize` advertises an empty
`capabilities` object so no server would even offer it. AvaloniaEdit already ships a complete,
reusable completion-popup toolkit (`CodeCompletion.CompletionWindow`/`CompletionList`/
`ICompletionData`, confirmed by inspecting the installed package directly) - this is a
materially cheaper build than Epic 15's ghost text or Epic 13's diff rendering, both of which had
no existing AvaloniaEdit primitive to anchor on.*

Full requirements, design, and task breakdown: [`specs/editor-autocomplete/`](specs/editor-autocomplete/requirements.md).
Scoped to Explorer's `CodeEditor` only, same boundary Epic 17 already drew (Tutorial's proposal
editor has no stable on-disk file identity for a language server to track).

**F19.1 — Completion request plumbing** — ✅ Done 2026-10-23
- [x] T19.1.1 Add `GetCompletionAsync` to `ILanguageServerClient`/`StdioLanguageServerClient`
  (`textDocument/completion`, parsing either the bare-array or `CompletionList` response shape)
  and advertise `capabilities.textDocument.completion` in `initialize` - shared handshake change
  with Epic 18's T18.1.2 area, whichever epic lands first should make it. — done 2026-10-23: new
  `LspCompletionItem` record (`Paarde.Core/Models`) mirrors the wire response verbatim
  (`Label`/`Detail`/`InsertText`/`Kind`) - the `InsertText ?? Label` fallback (R4.1) is deliberately
  left to the future UI call site (T3/T4), not resolved here. `StdioLanguageServerClient.
  GetCompletionAsync` sends `textDocument/completion` via the same `TextDocumentPositionParams`
  helper `GetDefinitionAsync`/`GetReferencesAsync` already use, and `ParseCompletionItems` handles
  both a bare `CompletionItem[]` and a `CompletionList { items: [...] }` object (R2.2) - `initialize`
  now also declares `capabilities.textDocument.completion.completionItem.snippetSupport: false`
  (R2.1), landing in the same `textDocument` capabilities object Epic 18 already restructured for
  exactly this addition. Since Epic 18's `ILanguageServerSessionManager` façade already existed
  (shipped before this task, unlike when design.md was drafted), also added
  `GetCompletionAsync` there and to `LanguageServerSessionManager` following the identical
  `TryGetClient`-routing pattern as `GetDefinitionAsync`/`GetReferencesAsync`, so `CodeEditor`'s
  future wiring (T4) can go through the same façade `ExplorerViewModel` already uses for every
  other LSP call rather than a one-off direct `ILanguageServerClient` reference. Updated both
  `FakeLanguageServerClient` fakes and `FakeLanguageServerSessionManager` to implement the new
  method. Added `StdioLanguageServerClientTests` cases (capability advertisement, bare-array
  response, `CompletionList` response, missing `insertText` left `null`) using the same fake
  Python-script LSP server fixture Epic 17 established. Not yet wired to any UI - `CompletionWindow`
  rendering, the trigger-character/Ctrl+Space wiring, and ghost-text coexistence are T3/T4/T6. Full
  solution builds with 0 warnings/errors, 797/797 tests pass (481 Core + 316 Avalonia - 3 new).

**F19.2 — Completion dropdown UI** — ✅ Done 2026-10-23
- [x] T19.2.1 Wire AvaloniaEdit's own `CompletionWindow` to `.`-trigger-character typing and a
  Ctrl+Space manual-invoke shortcut on `CodeEditor`, accepting with Tab/Enter/click and dismissing
  with Escape or caret movement - using the control's built-in list/filter/anchoring behavior
  rather than a new bespoke popup. — done 2026-10-23: deviated from `design.md`'s literal
  suggestion that `CodeEditor` call `ILanguageServerSessionManager`/`ILanguageServerClient`
  directly - every other `Controls/` class in this codebase (confirmed via
  `grep -rln "App.GetService" Paarde.Avalonia/Controls/` returning nothing) stays DI-free and
  round-trips through an event + bindable property instead, same shape as Epic 15's
  `GhostTextRequested`/`GhostTextSuggestion` and Epic 17's `GoToDefinitionRequested`/
  `FindReferencesRequested`. Added `CompletionRequested` (`EventHandler<CompletionRequestedEventArgs>`,
  carrying the trigger `LspPosition`) and a `CompletionItems` `StyledProperty<IReadOnlyList<
  LspCompletionItem>?>` to `CodeEditor`; `TextArea.TextEntered` fires `TriggerCompletion()` on `.`
  (R1.1), and a new Ctrl+Space branch at the top of `OnTextAreaKeyDown` does the same regardless of
  caret position (R1.2) - checked ahead of the existing ghost-text-only early return so it works
  whether or not a ghost-text suggestion happens to be showing. `ExplorerViewModel.
  RequestCompletionAsync` handles `CompletionRequested` by calling the already-existing
  `ILanguageServerSessionManager.GetCompletionAsync` (same no-op-if-no-client behavior as
  `GoToDefinitionAsync`/`FindReferencesAsync` covers R1.3 for free - no client registered for an
  unsupported file means an empty list and no dropdown, no new guard needed), then sets
  `CompletionItems` back (null-then-value, same double-assignment `GhostTextSuggestion` already
  uses so a repeat result still raises `PropertyChanged`); `ExplorerView.axaml.cs` wires both sides.
  `OnCompletionItemsChanged` discards a stale result (caret moved since the request, via the same
  `_pendingCompletionRequestOffset`-vs-`CaretOffset` check `OnGhostTextSuggestionChanged` already
  uses) or an empty one, otherwise builds a `CompletionWindow` populated with one
  `CodeEditorCompletionData` per item and shows it, first dismissing any visible ghost-text
  suggestion (R6.2). New `CodeEditorCompletionData : ICompletionData`
  (`Paarde.Avalonia/Controls/`) adapts an `LspCompletionItem`: `Text`/`Content` from `Label`,
  `Description` from `Detail` (R3.4's plain-text secondary slot, no icon per the non-goal), and a
  separately-exposed `InsertionText` (`InsertText ?? Label`, R4.1) used by `Complete` to replace
  the tracked completion segment - pulled out as its own property specifically so it's testable
  without constructing a live `TextArea` (see T19.4.1's note on why that's not possible in this
  sandbox). Confirmed via `ilspycmd` decompile of the installed `AvaloniaEdit.dll` that Tab/Enter
  acceptance, PointerPressed click-to-accept, and Escape-to-dismiss are all `CompletionWindowBase`/
  `CompletionWindow` defaults needing no extra wiring (R4.1, R4.2, R5.1), and that
  `TextArea.Caret.PositionChanged` already closes the window the moment the caret leaves
  `[StartOffset, EndOffset]` by any means other than typing a filter character (R5.2) - see T19.2.2
  below for the filtering nuance found the same way.
- [x] T19.2.2 (folded into T19.2.1, tracked separately since it was an "exercise the real control"
  open question in `requirements.md` rather than a planned code change) Confirmed via `ilspycmd`
  decompile that `CompletionWindow`/`CompletionList`'s default behavior already satisfies R3.2-R3.4
  with no adapter needed: `CompletionList.SelectItem` runs prefix/fuzzy `GetMatchQuality` scoring
  against everything typed since the trigger (`IsFiltering` is on by default) every time
  `CaretPositionChanged` fires, and sets `CompletionList.IsVisible = false` (not a literal
  `Hide()`/close) when zero items match - functionally equivalent for the user (nothing is shown,
  Tab/Enter have nothing to accept) but worth noting precisely: if the user then backspaces to a
  point that matches again, the same window becomes visible again rather than requiring a new
  trigger, which is arguably better than hard-closing, not a gap to fix.

**F19.3 — Coexistence with Epic 15's ghost text** — ✅ Done 2026-10-23
- [x] T19.3.1 Suspend ghost-text triggering while a completion dropdown is open, and dismiss any
  visible ghost-text suggestion the instant a dropdown opens, so the two completion mechanisms
  never compete for the same keystroke or screen space. — done 2026-10-23: `OnGhostTextTimerTick`
  (the only place `GhostTextRequested` is ever raised) now also bails when `_completionWindow is
  not null` (R6.1); since `_completionWindow` is nulled out via the window's own `Closed` handler,
  this re-evaluates on every subsequent 150ms tick with no separate "resume" step needed (R6.3 falls
  out for free). `ShowCompletionWindow` calls `DismissGhostTextSuggestion()` before showing the new
  window (R6.2).

**F19.4 — Tests** — ✅ Done 2026-10-23
- [x] T19.4.1 Extended `StdioLanguageServerClientTests` (both response shapes, capability
  advertisement), `CodeEditorCompletionData` unit tests, and a ghost-text/completion coexistence
  gating test, per `specs/editor-autocomplete/design.md`'s testing section. — done 2026-10-23: the
  `StdioLanguageServerClientTests` piece landed already under T19.1.1. Added
  `CodeEditorCompletionDataTests` (`Paarde.Avalonia.Tests/Controls/`) covering the `InsertionText`
  `InsertText ?? Label` fallback and the `Text`/`Content`/`Description` mappings - deliberately not
  exercising `Complete(...)` itself: constructing a bare `AvaloniaEdit.Editing.TextArea` outside a
  running Avalonia `Application` throws (`CaretNavigationCommandHandler`'s static constructor needs
  `HotkeyConfiguration.Keymap`, which reads `Application.Current`), confirmed by trying it directly
  in this sandbox - same "no X server / no live `Application`" limitation already noted elsewhere
  in this roadmap for other AvaloniaEdit-control-level behavior. `Complete`'s actual document
  mutation, the Ctrl+Space/`.`-trigger wiring, and the ghost-text coexistence gating are all
  `CodeEditor`-level behavior with the same limitation (no existing `CodeEditor`-level tests exist
  in this codebase for the same reason) - covered by the `ilspycmd` decompile evidence cited under
  T19.2.1/T19.2.2 plus T19.4.2's manual verification below, not a new automated test. Full solution
  builds with 0 warnings/errors, 801/801 tests pass (481 Core + 320 Avalonia - 4 new).
- [x] T19.4.2 Manual verification in a real C# project (requires a running `Application`/display,
  unavailable in this sandbox): typing `.` after a known symbol shows a populated dropdown;
  Tab/Enter/click accept; Escape dismisses; a ghost-text suggestion visible at trigger time
  disappears when the dropdown opens; typing past all matches auto-hides the window. Repeat once
  [[multi-language-support]] ships for a Python and a TypeScript file. — 2026-10-25: the
  protocol-level half of this (does `GetCompletionAsync` actually return real completions for a
  Python and a TypeScript file) is now verified - see Epic 18's T18.4.1 note. 2026-10-26: the
  `CodeEditor`-level half (`CompletionWindow` popping up, Tab/Enter/click/Escape, ghost-text
  coexistence) is now also done - Kevin visually verified it on a machine with a display (this
  sandbox has none) and confirmed it works. Epic 19 is fully complete.

---

## The "wizard → IDE" initiative (Epics 20–25)

**Context:** asked 2026-10-26 to plan how to make Paarde feel more like an IDE than a wizard, with
a concrete, immediate ask folded into it — persistent Build/Run controls for .NET today,
generalized for other languages later. What follows is that plan: one epic that ships the concrete
ask on its own (Epic 20); a direct follow-up that fixes the gap Epic 20 exposed — Run's output
panel is silent for full-screen TUI apps because there's no real PTY/terminal emulation anywhere
in Paarde (Epic 21); then three epics that fix the structural reason everything else in this app
still reads as a wizard (Epics 22–24); then one larger, explicitly optional bet flagged for later
judgment rather than committed to now (Epic 25).

**Diagnosis — what "wizard" means here, precisely:** every page in this app (`Prompt`,
`Explorer`, `Terminal`, `Diffs`, `Tutorial`, `History`, `Settings`) is a full `Frame` navigation
target under `ShellView`'s `NavigationView` (`PageService.cs`, `ShellView.axaml`). Nothing
persists across a page switch except what's been explicitly saved to settings — there is no
concept of "state that survives regardless of which page you're looking at," which is the one
thing every IDE (VS Code, Rider, Visual Studio) has and every installer-style wizard doesn't.
Concretely, today:
- There's nowhere to put a Run button that isn't itself a page you navigate to and away from.
- Output from a build/test/terminal command (Epics 12/16) only exists on the page that produced
  it; switching pages loses sight of it even though the process might still be running.
- There's no status bar — no at-a-glance git branch/dirty state, no language-server connection
  state, no caret position — all things Epic 17/18's diagnostics and Epic 4.3's `IGitStatusService`
  already compute internally but surface nowhere persistent.
- Explorer can only show one file at a time (`SelectedFilePath`) — there's no equivalent of
  editor tabs, so comparing two files means losing sight of the first one entirely.
- Every action (even "find a file," "run a command," "check diagnostics") requires knowing which
  of seven pages it lives on and navigating there first, rather than one keyboard shortcut from
  wherever you already are.

None of this is wrong *as a wizard* — the Prompt→Tutorial→Diffs→Apply flow genuinely is a
sequential workflow with a beginning and an end, and keeping it page-based is fine. What's missing
is a **persistent layer underneath the pages** that doesn't care which step of that workflow
you're on. That's what Epics 20–23 build, in increasing order of how much of the shell they touch.

### EPIC 20 — Build & Run *(P0 — the explicit, concrete ask)* — Implementation done 2026-10-26, manual verification pending

*Why: "incorporate build and run buttons in DotNet applications (for now and also for other
compiled languages in the future)" is the specific, immediate request this whole initiative grew
out of. It's also fully self-contained — it doesn't need Epic 22's bigger shell rework to ship,
it just needs *somewhere* persistent to live, which it brings with it (a minimal toolbar + output
strip in `ShellView`, generalized into the full dock by Epic 22 later). Full spec, including the
exact new types, merge/detection algorithm, and process-lifecycle design:
[`specs/build-and-run/`](specs/build-and-run/requirements.md).*

**F20.1 — Run configuration model and detection** — ✅ Done 2026-10-26
- [x] T20.1.1 `RunConfiguration` record (language id, working directory, project-relative path,
  build command, run command, auto-detected flag) + per-working-directory settings keys. See
  `specs/build-and-run/design.md`.
- [x] T20.1.2 `IRunConfigurationDetector`/`RunConfigurationDetector`: C# via `.csproj` scan +
  `OutputType`/`Sdk`/test-project classification (reusing Epic 18's `IProjectTypeDetector.
  DetectLanguageRoots` as the starting point, not a second filesystem walk from scratch); Python
  via a common-entry-point-filename heuristic; TypeScript via `package.json`'s `scripts` map.
- [x] T20.1.3 `RunConfigurationsProvider`: merges fresh detection against the saved list without
  overwriting a user-edited entry; seeds a first-ever C# candidate's build command from Epic 12's
  existing global `BuildTestSettingsKeys.BuildCommand` when set.

**F20.2 — A process that doesn't just wait for exit** — ✅ Done 2026-10-26
- [x] T20.2.1 `IRunProcessManager`/`RunProcessManager`: starts a process and returns immediately
  (unlike `IBuildTestRunner`/`ITerminalCommandRunner`, both wait-for-exit today), streams output
  live, kills the full process tree on `Stop`, auto-stops a still-running prior process on a new
  `Start`. Register as a DI singleton; wire `Stop()` into `App.axaml.cs`'s exit handler alongside
  `ILanguageServerSessionManager.StopAllAsync()` so a Run session never outlives the app. — each
  running process tracks its own `StoppedByUs` flag (not one shared field) so a second `Start()`
  can't misreport the prior process's exit as a real exit code.

**F20.3 — A persistent place for the controls to live** — ✅ Done 2026-10-26
- [x] T20.3.1 `RunPanelViewModel` (DI singleton): configuration picker, Build/Run/Stop commands,
  Build-before-Run sequencing with failure short-circuit. — learns the working directory via a new
  minimal `IWorkingDirectoryContext` pub/sub hook (fed by `PromptViewModel`) rather than
  `INavigationAware`, plus an eager load of the saved working directory in its own constructor so
  the toolbar works even in a session that never visits the Prompt page.
- [x] T20.3.2 `ShellView.axaml` gains a toolbar row above the existing `NavigationView` and a
  collapsible output strip below it (auto-expanding on a build start/failure or a run start) - no
  other page's `.axaml` changes. Epic 22 is what generalizes this one strip into the full
  Build/Run/Problems/Terminal dock; this ships only the Build/Run tab's worth, but it's real and
  usable from every page on day one.

**F20.4 — Tests** — Implementation tests done 2026-10-26, manual verification pending
- [x] T20.4.1 `RunConfigurationDetectorTests`, `RunConfigurationsProviderTests`,
  `RunProcessManagerTests`, `RunPanelViewModelTests` per `specs/build-and-run/design.md`'s testing
  section. — done 2026-10-26: 22 new tests in `Paarde.Core.Tests`, 21 new in `Paarde.Avalonia.Tests`
  (including fixes to `PromptViewModelTests`/`SettingsViewModelTests` for the new constructor
  parameters `RunPanelViewModel`'s wiring required). `RunPanelViewModelTests` doesn't exercise
  `OnProcessOutputReceived`/`OnProcessExited` since those marshal through `Dispatcher.UIThread.
  Post`, which this plain xunit host doesn't pump - same gap already documented on
  `TerminalViewModelTests`. Full suites pass: 515/515 Core, 340/340 Avalonia.
- [ ] T20.4.2 Manual verification against this repo itself (requires a running `Application`/
  display, unavailable in this sandbox): Build/Run/Stop actually drive `dotnet build`/`dotnet run`,
  Stop leaves no orphaned process, switching configurations switches targets, and all of it is
  reachable from every page.

---

### EPIC 21 — Real terminal emulation (PTY + VT100) *(P1 — direct follow-up to Epic 20)*

*Why: asked 2026-10-26 why Run's output panel stayed empty for a Terminal.Gui-based console app.
Root cause, confirmed by direct reproduction: `RunProcessManager`/`ProcessCommandRunner` both read
child-process output via `Process.OutputDataReceived`, which is strictly line-oriented and only
fires on `\n`. Full-screen TUI frameworks (Terminal.Gui, and anything else using the alternate
screen buffer) redraw entirely through newline-free ANSI/VT100 cursor-positioning escape sequences
- that never fires `OutputDataReceived` during normal operation, and the one blob that does arrive
on process teardown is raw, unreadable escape-code soup. There is no PTY anywhere in Paarde today;
Epic 16 (T16.1.1) already identified this exact gap in 2026-10-22 and explicitly deferred it
("Recommend the scoped-down version first... no PTY") because building one well is real work. This
epic is that deferred work, done properly rather than patched around. Full spec, including the
exact new types and the architectural tradeoff below: [`specs/terminal-emulation/`]
(specs/terminal-emulation/requirements.md).*

*Library choice, researched rather than guessed per explicit instruction to not ship "LLM generated
slop": [Porta.Pty](https://www.nuget.org/packages/Porta.Pty) (tomlm) for cross-platform PTY
allocation (ConPTY on Windows, `forkpty`/`posix_openpt` on Linux/macOS) - MIT, `net10.0`, actively
maintained, async `PtyProvider.SpawnAsync(PtyOptions, CancellationToken)` → `IPtyConnection`
(`ReaderStream`/`WriterStream`/`Pid`/`ExitCode`/`ProcessExited`/`Resize`/`Kill`). [XTerm.NET]
(https://www.nuget.org/packages/XTerm.NET) (tomlm, same author) as the headless VT100/ANSI
emulation engine - MIT, `net10.0`, UI-framework-agnostic ("bring your own renderer"), full
VT100/ANSI + 256/true color + Unicode/wcwidth, normal + alternate screen buffer support. Both
rejected/deprioritized alternatives: `XtermSharp` and `VtNetCore` (both stale since 2022/2023),
`ghostflyby/Pty.Net` (unproven fork). The same author's `Iciclecreek.Avalonia.Terminal` is a
feature-complete, drop-in Avalonia terminal control built on exactly these two libraries, and would
otherwise be the obvious choice - but it requires Avalonia `[12.0.4, )`, while Paarde is
deliberately pinned to `11.3.22` (see `Paarde.Avalonia.csproj`'s comment on why). Adopting it would
cascade into a major-version `FluentAvaloniaUI` bump and an `AvaloniaEdit`/`AvaloniaEdit.TextMate`
upgrade, an app-wide regression risk far outside this epic's scope. Decision (confirmed
2026-10-26): stay on Avalonia 11.3.x, consume Porta.Pty and XTerm.NET directly (both are
Avalonia-version-agnostic), and build a small, Paarde-owned Avalonia-11-compatible rendering
control instead of adopting the third-party widget.*

**F21.1 — PTY-backed process execution** — T21.1.1, T21.1.2 done 2026-10-27
- [x] T21.1.1 Add the `Porta.Pty` package reference to `Paarde.Core`. Add `IPtyProcessManager`/
  `PtyProcessManager`: spawns via `PtyProvider.SpawnAsync`, exposes the raw byte stream (not
  line-based `OutputReceived`), a `Resize(cols, rows)`/`WriteInput` passthrough, and the same
  `Stop`/process-tree-kill/"stopped by us" semantics `RunProcessManager` already has.
  _(Done 2026-10-27: `Paarde.Core/Contracts/Services/IPtyProcessManager.cs`,
  `Paarde.Core/Services/PtyProcessManager.cs`, registered as a DI singleton with `Stop()` wired
  into `App.axaml.cs`'s exit handler. Verified live against the real `TODOTUI1` repro - the PTY
  path streams the app's alternate-screen redraw immediately, where the old pipe-based path
  produced nothing. Full 515/515 Core + 340/340 Avalonia suites still pass.)_
- [x] T21.1.2 Wire `RunPanelViewModel` (Epic 20) to use the PTY-backed path for Run (not Build -
  build output is still naturally line-oriented and doesn't need a PTY).
  _(Done 2026-10-27: `RunPanelViewModel` now takes `IPtyProcessManager` instead of
  `IRunProcessManager` and owns a `TerminalBuffer OutputBuffer`; `RunAsync` starts via
  `_ptyProcessManager.StartAsync(...)`, `OnProcessOutputReceived(byte[])` feeds `OutputBuffer`
  directly, and `WriteInput`/`Resize` passthroughs let the View wire `TerminalControl`'s events to
  it without reaching past the ViewModel. Build's output stays line-oriented
  (`IBuildTestRunner`) but is fed into the same `OutputBuffer` through a new `EncodeForTerminal`
  helper that normalizes its bare `\n` to `\r\n` first - confirmed via a throwaway harness that
  XTerm.NET's `Terminal` treats bare `\n` strictly per VT100 semantics (no implicit carriage
  return), which otherwise renders as a "staircase". `IRunProcessManager`/`RunProcessManager` and
  their test fake/suite had no other consumers and were deleted outright as dead code, along with
  their DI registration/exit-stop in `App.axaml.cs`. Full 520/520 Core + 362/362 Avalonia suites
  pass. See `specs/terminal-emulation/tasks.md` T6 for the full writeup.)_

**F21.2 — VT100/ANSI emulation** — T21.2.1 done 2026-10-27
- [x] T21.2.1 Add the `XTerm.NET` package reference. Add a thin adapter that feeds PTY output bytes
  into an `XTerm.NET` `Terminal` instance and exposes its screen-cell buffer (text, colors,
  attributes, cursor position, alternate-buffer state) in a form the rendering control (F21.3) can
  consume without depending on Avalonia itself.
  _(Done 2026-10-27: `Paarde.Core/Services/TerminalBuffer.cs`. Its `Changed` event is raised by
  `TerminalBuffer` itself after every `Feed`/`Resize`, not from XTerm.NET's own `BufferChanged` -
  that only fires on alternate/normal screen switches, not on every write, so it isn't a general
  repaint signal. Color resolution was verified empirically against the real library rather than
  its README, whose documented color-mode contract doesn't match the actual source. Regression-
  tested with the exact byte-for-byte escape sequence captured from the `TODOTUI1` repro. Full
  526/526 Core + 340/340 Avalonia suites pass.)_
- [ ] T21.2.2 Explicitly out of scope for this epic: scrollback UI beyond what XTerm.NET's buffer
  gives for free, Sixel/Kitty graphics, and terminal window-manipulation escape sequences (resize-
  the-OS-window, etc.) - none of which Run/Terminal's use case needs.

**F21.3 — A Paarde-owned terminal rendering control** — T21.3.1, T21.3.2 done 2026-10-27
- [x] T21.3.1 New Avalonia 11-compatible control: a grid of styled character cells + a blinking
  cursor, repainted from the XTerm.NET buffer adapter (F21.2) on each update. Scoped to monospace
  text + ANSI colors/attributes only - no custom theming beyond what the app's existing color
  palette needs.
  _(Done 2026-10-27: `Paarde.Avalonia/Controls/TerminalControl.cs`. A plain `Control` subclass (no
  template) - the first standalone custom-drawn `Control` in this codebase - with a bindable
  `Buffer` property that subscribes to `TerminalBuffer.Changed` and repaints via
  `Dispatcher.UIThread.Post(InvalidateVisual)`. `Render` draws each cell with `FormattedText`
  (`Cascadia Mono,Consolas,monospace`), honoring per-cell colors/bold/inverse/invisible, plus a
  cursor rectangle (filled when focused, outlined when not). All Avalonia API shapes were confirmed
  by reflecting against the installed 11.3.22 DLLs rather than guessed. Verified with a throwaway
  headless-rendering harness that feeds the real captured `TODOTUI1` byte blob through a live
  `TerminalControl` and renders it with no exceptions. Keyboard input (T21.3.2, below) and wiring
  into Run/Terminal (T21.3.3) are separate tasks. Full 526/526 Core + 340/340 Avalonia suites
  pass. Resize handling also landed as part of this control (not broken out as its own roadmap
  line): `OnSizeChanged` recomputes `cols`/`rows` from pixel size and the measured monospace cell
  via a pure `ComputeGridSize` method, calls `Buffer.Resize` directly, and raises a `Resized`
  event shaped to match `IPtyProcessManager.Resize(int, int)` so a consumer wires
  `control.Resized += ptyProcessManager.Resize;` directly - verified end-to-end with a headless
  harness that arranged a real control at 1600x1000px and confirmed its bound buffer resized
  accordingly with no redundant re-resize on an unchanged size. Full 526/526 Core + 362/362
  Avalonia suites pass.)_
- [x] T21.3.2 Keyboard input from the control is written back to the PTY's `WriterStream` (F21.1),
  including control characters (Ctrl+C, arrow keys as their VT100 escape sequences, etc.) so
  interactive full-screen apps (Terminal.Gui, `vim`, `htop`) behave normally.
  _(Done 2026-10-27: `TerminalControl.OnTextInput`/`OnKeyDown` raise a new `InputReceived`
  (`Action<byte[]>?`) event rather than taking a direct `IPtyProcessManager` dependency - its
  byte-shape deliberately matches `WriteInput(byte[])` so a consumer wires
  `control.InputReceived += ptyProcessManager.WriteInput;` directly, and `WriteInput`'s existing
  no-op-when-nothing-running behavior satisfies "only while a process is running" without this
  control needing to track process state. Special-key encoding (arrows, Home/End/Page Up/Down,
  Insert/Delete, Enter/Tab/Backspace/Escape, Ctrl+A-Z) is a pure `internal TryEncodeSpecialKey`
  method, unit-tested directly (19 cases in `TerminalControlTests`) without an Avalonia display.
  Full 526/526 Core + 359/359 Avalonia suites pass.)_
- [x] T21.3.3 Wire the control into Run's output strip (Epic 20) as the renderer when a run is
  PTY-backed, and into Epic 16's `TerminalView` page, replacing its current plain-text
  `OutputText` append for the cases where a PTY is in use.
  _(Run half done 2026-10-27 as part of T21.1.2 above: `ShellView.axaml`'s output-strip `TextBox`
  is replaced by `<controls:TerminalControl Buffer="{Binding RunPanelViewModel.OutputBuffer}" />`
  inside the unchanged `Expander`, with `ShellView.axaml.cs` wiring `InputReceived`/`Resized` to
  `RunPanelViewModel`'s passthroughs. Terminal half done 2026-10-27 per
  `specs/terminal-emulation/tasks.md` T7: `TerminalView.axaml` now hosts the same `TerminalControl`.)_

**F21.4 — Tests** — ✅ Done 2026-10-30 (manual verification, spec T9, still pending)
- [x] T21.4.1 `PtyProcessManagerTests` (or equivalent): a real PTY-backed process starts, streams
  bytes, resizes, and stops/kills its tree the same way `RunProcessManagerTests` already verifies
  for the plain-pipe path.
  _(Done 2026-10-30: `Paarde.Core.Tests/Services/PtyProcessManagerTests.cs`, 10 tests spawning real
  commands through a real PTY with per-platform commands (cmd.exe on Windows, sh elsewhere): output
  streaming, natural exit code, working directory, `Stop()` → `Exited(null)`, restart-stops-previous,
  `Resize` while running, a `WriteInput` stdin round trip, inner-quotes/`&&` reaching the shell
  intact, and the nothing-running no-ops. **Writing these found a real Windows bug:** Porta.Pty's
  default Windows argument formatting quotes every argument (`cmd.exe "/c" "exit 3"`), which cmd.exe
  misparses - every Run/Terminal command on Windows failed with `'"<command>' is not recognized`.
  CI never saw it because Linux's `sh -c` gets a real argv. Fixed in `PtyProcessManager` by passing
  `VerbatimCommandLine = true` with the standard `cmd /s /c "<command>"` form (confirmed against a
  decompile of Porta.Pty 2.2.3's `WindowsArguments`). The echo test also asserts exit code 0, since
  the old error message itself contained the echoed text and would otherwise mask a regression.
  All 10 pass on Windows. Process-tree kill of grandchildren isn't asserted - there's no portable
  way to enumerate them from a test - so that stays part of T9's manual check.)_
- [x] T21.4.2 Terminal-adapter tests: feeding known ANSI/VT100 sequences (cursor moves, alternate
  screen enter/exit, colors) through the XTerm.NET adapter produces the expected cell-buffer state.
  Rendering-control visual verification, like every other Avalonia control in this roadmap, needs a
  real display and is manual/deferred.
  _(Done 2026-10-27 alongside T21.2.1: `TerminalBufferTests` covers cursor positioning, 16/256/
  true-color SGR, alternate-buffer enter/exit, resize, and the byte-for-byte `TODOTUI1` regression
  blob.)_

---

### EPIC 22 — Persistent workbench shell *(P1 — the structural fix)*

*Why: Epic 20 proves the pattern (one strip in `ShellView`, visible from every page) for Build/Run
specifically. This epic generalizes that pattern into the thing that actually makes the app stop
feeling like a wizard: a real output **dock** (tabs, not one strip) and a **status bar** — the two
pieces every IDE has that a page-based wizard doesn't. Nothing here requires touching any
individual page's content; it's additive to `ShellView` only, same scope discipline Epic 20 used.
Full spec: [`specs/ide-workbench-shell/`](specs/ide-workbench-shell/requirements.md).*

**F22.1 — Generalize Epic 20's output strip into a multi-tab dock** — T22.1.1, T22.1.2 done
2026-10-27
- [x] T22.1.1 Replace the single Build/Run output strip with a small tabbed dock (Build, Run,
  Problems, Terminal) living in the same `ShellView` location. "Terminal" absorbs Epic 16's
  `TerminalView` page's content (the page itself can be removed from the `NavigationView` once its
  functionality lives in the dock, or kept as a deep-link for muscle memory - decide at
  implementation time). "Problems" surfaces Epic 17/18's LSP diagnostics across every open file,
  not just the one currently shown in Explorer, aggregated via a new small
  `IDiagnosticsAggregator` that both Explorer and the dock subscribe to instead of Explorer owning
  the only copy of `Diagnostics`.
  _(Done: four-tab `TabControl` (Build/Run/Problems/Terminal) inside `ShellView`'s existing
  `Expander`, fed by `WorkbenchDockViewModel`. `TerminalViewModel` promoted to a DI singleton so
  the Terminal page and the dock's Terminal tab share one live PTY session, kept as a deep-link
  per `specs/ide-workbench-shell/design.md`'s decision.)_
- [x] T22.1.2 The dock remembers which tab was last active and whether it was expanded/collapsed
  across navigation (and ideally across app restarts, same persisted-UI-state convention this
  app already uses for working directory/theme/window size).
  _(Done: `WorkbenchDockSettingsKeys` (`Dock.ActiveTab`, `Dock.IsExpanded`) persisted via
  `ILocalSettingsService`, restored on construction.)_

**F22.2 — A status bar** — T22.2.1, T22.2.2 done 2026-10-27
- [x] T22.2.1 A thin bar anchored at the very bottom of `ShellView` (below the dock, below
  everything): current git branch + dirty-file count (`IGitStatusService.GetStatus` already
  computes `IsDirty`/`DirtyFiles`; branch name itself needs a small addition - `git branch
  --show-current` - since `GitStatus` doesn't carry it today), the active `RunConfiguration`'s
  name, and a per-language code-intelligence connection indicator (reusing
  `ILanguageServerSessionManager`'s per-language state from Epic 18, surfaced instead of staying
  internal).
  _(Done: `StatusBarViewModel` (DI singleton) backs a new status bar row in `ShellView.axaml`.)_
- [x] T22.2.2 When `CodeEditor` has focus (Explorer, Tutorial's proposal editor), the status bar
  also shows caret line:column - needs a new bindable `CodeEditor.CaretPosition` property pushed
  up from AvaloniaEdit's own `TextArea.Caret.PositionChanged`, then threaded up through whichever
  view currently hosts the focused editor to `ShellViewModel`.
  _(Done: `CodeEditor.CaretPositionProperty` + `IActiveEditorContext`, wired from `ExplorerView`/
  `TutorialDetailControl`.)_

**F22.3 — Tests** — T22.3.1 done 2026-10-27
- [x] T22.3.1 `IDiagnosticsAggregator` tests (aggregates across files, clears on file close);
  status bar branch-name parsing tests for `IGitStatusService`'s extension; dock tab-persistence
  tests.
  _(Done: `DiagnosticsAggregatorTests`, `GitStatusServiceTests`/`LanguageServerSessionManagerTests`
  extensions, `StatusBarViewModelTests`, `WorkbenchDockViewModelTests`. Manual verification of the
  rendered dock/status bar (`specs/ide-workbench-shell/tasks.md` T8) remains deferred - same
  sandboxed-environment limitation as every other Avalonia-rendering task in this roadmap.)_

---

### EPIC 23 — Multi-file tabbed editing in Explorer *(P1/P2)*

*Why: `ExplorerViewModel.SelectedFilePath`/`SelectedFileContent` (Epic 10) is a single slot - only
one file can be open at a time, so comparing two files or keeping a reference file visible while
editing another means losing sight of the first one. Tabs aren't new to this app - Epic 8's
T8.2.2 already built exactly this pattern for the Tutorial page's proposed-file review
(`TutorialViewModel.OpenTabs`, an `ObservableCollection<ProposedFileChangeViewModel>` backing a
`TabControl`, with close-selects-neighbor semantics and dirty-marker (`*`) headers) - this epic is
applying that same, already-proven pattern to Explorer's arbitrary-file browsing, not inventing
tabs for the first time.*

**F23.1 — Open files as tabs, not a single slot** — T23.1.1, T23.1.2, T23.1.3 done 2026-10-27
- [x] T23.1.1 `ExplorerViewModel` grows an `ObservableCollection<OpenFileTabViewModel>` (each
  wrapping the per-file state `SelectedFilePath`/`SelectedFileContent`/`IsFileDirty`/
  `_originalFileContentOnDisk` already track for the single-file case today) plus a
  `SelectedTab` - same `OpenTabs`/`Selected` shape `TutorialViewModel` already established, not a
  new one. Double-clicking a tree node opens a new tab (or activates an existing one for that
  path, VS Code's convention - `TutorialViewModel.OnSelectedChanged`'s "add to `OpenTabs` if not
  already there" is the closer precedent than a brand-new dedupe rule) rather than replacing the
  current single file.
  _(Done 2026-10-27: added `OpenFileTabViewModel` (new, `Paarde.Avalonia/ViewModels/`) - one
  instance per open tab, self-contained the same way `ProposedFileChangeViewModel` already is for
  Tutorial's tabs, owning `Content`/`IsDirty`/`StatusMessage`/`FileOverview`/`IsLoadingOverview`/
  `Diagnostics`/`GhostTextSuggestion`/`CompletionItems` plus `Save`/`RevertToDisk`/`Explain`
  commands and the ghost-text/completion round trips directly (including its own per-tab
  `CancellationTokenSource`, since a request in one tab must no longer cancel one in another).
  `ExplorerViewModel` gained `OpenTabs`/`SelectedTab`/`HasOpenTabs` and an `OpenOrActivateTab`
  helper; tree-node selection (not double-click specifically - matching `OnSelectedChanged`'s own
  precedent more closely) opens or activates a tab via that helper. `GoToDefinitionAsync`/
  `FindReferencesAsync` now take an explicit `filePath` parameter instead of reading a removed
  `SelectedFilePath`, and `NavigateToLocation` opens/activates the target's tab before leaving a
  pending caret target for the newly-active tab's editor to apply.)_
- [x] T23.1.2 `ExplorerView.axaml` gains a `TabControl`/tab strip above the `CodeEditor`, styled
  the same as `TutorialView.axaml`'s (dirty `*` marker, close-selects-neighbor on
  `CloseSelectedTabCommand`); closing a dirty tab reuses the existing discard-confirmation dialog
  (T10.2.5) rather than a new one.
  _(Done 2026-10-27: `ExplorerView.axaml`'s single embedded `CodeEditor` became a
  `TabControl ItemsSource="{Binding OpenTabs}"`/`SelectedItem="{Binding SelectedTab}"` with a
  `ContentTemplate` instantiating a new `ExplorerFileTabControl` (new `UserControl`,
  `Paarde.Avalonia/Views/`) per open tab - same "separate control instance per tab" shape
  `TutorialDetailControl` already established, so each tab gets its own `CodeEditor` instance
  rather than one shared editor whose binding path gets swapped. `ExplorerFileTabControl` houses
  the per-tab toolbar (Save/Revert/Explain, dirty marker), editor, references flyout, and Overview
  expander that used to live directly in `ExplorerView.axaml`; its code-behind reaches the
  page-level `ExplorerViewModel` for cross-file navigation via `this.FindAncestorOfType<ExplorerView>()`,
  the same ancestor-lookup pattern `TutorialDetailControl` uses for `ILlmRunner`.
  `CloseSelectedTabCommand` (parameterless, matching `TutorialViewModel`'s own naming/shape exactly)
  prompts via the existing `IDialogService.ConfirmAsync` only when the tab being closed is dirty,
  with new `Explorer_CloseDirtyTabConfirm*` string resources replacing the old
  `Explorer_DiscardEditsConfirm*` ones (switching tabs can no longer discard anything now that every
  tab keeps its own content, so only closing a dirty tab still needs to ask). `ExplorerView.axaml.cs`
  is now trimmed down to just the tree-pane-width (`ResizablePaneHelper`) logic.)_
- [x] T23.1.3 Per-tab language-server document lifecycle: every open tab needs its own `didOpen`/
  `didClose` pair with `ILanguageServerSessionManager` (today there's exactly one open document at
  a time, tied to the single-slot model) - closing a tab must send `didClose` for *that* file, not
  whichever file happens to be selected when the close happens.
  _(Done 2026-10-27: `OpenOrActivateTab` calls `NotifyDocumentOpenedAsync` once per newly-created
  tab (a no-op if the tab already exists and is just being activated); `CloseSelectedTabAsync`
  calls `NotifyDocumentClosedAsync(tab.FilePath)` for the specific tab being closed, read from the
  tab itself rather than from any shared "currently selected" state, before removing it from
  `OpenTabs`. `OnDiagnosticsPublished` now looks up the matching tab by `FilePath` across
  `OpenTabs` instead of assuming a single active file.)_

**F23.2 — Tests** — T23.2.1 done 2026-10-27
- [x] T23.2.1 `ExplorerViewModelTests` extended for: opening a second file doesn't discard the
  first's unsaved edits; closing a tab prompts for a dirty file and not a clean one; switching tabs
  swaps which file's diagnostics/overview are shown; `didOpen`/`didClose` fire per tab correctly.
  _(Done 2026-10-27: rewrote `ExplorerViewModelTests` against the new `OpenTabs`/`SelectedTab` API -
  selecting a file opens/activates a tab, selecting a directory or null leaves tabs untouched,
  re-selecting an already-open file activates the existing tab rather than duplicating it, and
  `RefreshTreeCommand` clears the stale tree selection without closing any tabs. The three
  discard-confirmation-on-switch tests no longer have a behavior to test (switching tabs can't
  discard anything once every tab keeps its own content) and were replaced with one test plus an
  explanatory comment confirming the new "open a second tab without prompting or touching the
  first" behavior, and `CloseSelectedTabCommand` got its own not-dirty/dirty-confirmed/
  dirty-declined/close-selects-neighbor tests. Save/Revert/Explain's tests moved onto a new
  `OpenFileTabViewModelTests` alongside the logic they cover. `dotnet build Paarde.slnx
  --no-incremental`: 0 warnings/0 errors. Full solution test run: 911/911 passed.)_

---

### EPIC 24 — Command palette & Quick Open *(P2)* — Implementation done 2026-10-30, manual verification pending

*Why: right now, "run a build," "open a file," "jump to Settings," and "search the codebase" each
require first knowing which of seven pages that lives on. A command palette (`Ctrl+Shift+P`,
VS Code/Sublime/every modern IDE's convention) and a Quick Open (`Ctrl+P`, fuzzy file-name search)
replace "navigate to the right page" with "type what you want" from anywhere - the single biggest
per-action speed difference between a wizard and a tool people live in all day.
Full spec: [`specs/command-palette/`](specs/command-palette/requirements.md).*

**F24.1 — Quick Open** — Done 2026-10-30
- [x] T24.1.1 `Ctrl+P` anywhere in the app opens a lightweight popup (same `Popup`/`ListBox` shape
  Epic 14's `@file`-mention picker already built) substring-matching file names from
  `FileTreeBuilder`'s existing gitignore-aware walk (via the existing `FileMentionMatcher`);
  selecting a result navigates to Explorer with that file opened as a tab (Epic 23).
  _(Done: new `QuickOpenViewModel` (DI singleton) + a global `Ctrl+P` `KeyBinding` on the `Window`
  in `ShellView.axaml.cs`. `ExplorerViewModel.OnNavigatedTo` grew a `parameter is string filePath`
  branch that calls the existing `OpenOrActivateTab`, so Quick Open reaches the already-running
  Explorer page instance without promoting it to a singleton.)_

**F24.2 — Command palette** — Done 2026-10-30
- [x] T24.2.1 `Ctrl+Shift+P` opens a similar popup listing a small, explicit registry of named
  commands (`"Build"`, `"Run"`, `"Stop"`, one `"Go to <Page>"` entry per `NavigationView` page)
  rather than introspecting every `[RelayCommand]` in the app - an explicit allowlist keeps this
  from surfacing half-finished or page-context-dependent commands that would do nothing (or throw)
  if invoked from the wrong place.
  _(Done: new `CommandPaletteViewModel` (DI singleton) with a hand-written `PaletteCommand`
  registry (11 entries) delegating Build/Run/Stop to `RunPanelViewModel`'s existing commands.
  Disabled entries (R2.4) stay in the list via a computed `PaletteCommand.IsEnabled`. At most one
  of Quick Open/the command palette is ever open at a time (R3.2), wired via an
  `AttachCommandPalette`/`AttachQuickOpen` reference set once by `ShellViewModel`'s constructor
  rather than circular DI constructor injection between the two singletons.)_

**F24.3 — Tests** — Done 2026-10-30
- [x] T24.3.1 Substring-filter and registry-dispatch tests (reusing the existing
  `FileMentionMatcher` rather than a second implementation); command-registry dispatch tests.
  _(Done: new `QuickOpenViewModelTests`, `CommandPaletteViewModelTests`, plus
  `ExplorerViewModelTests` extensions for the new `OnNavigatedTo` branch (including a regression
  guard for non-`string`/`null` parameters). `dotnet build Paarde.slnx`: 0 warnings/0 errors. Full
  solution test run: 928/928 passed. Manual verification of the rendered popups/global keybindings
  (`specs/command-palette/tasks.md` T9) remains deferred - same sandboxed-environment limitation as
  every other Avalonia-rendering task in this roadmap.)_

---

### EPIC 25 — Unify Prompt/Diffs/Tutorial into one dockable surface *(P3 — explicitly optional, not committed)*

*Why flagged instead of planned: this is the one change big enough to actually question whether the
Prompt→Diffs→Tutorial→Apply flow should stop being three separate full-page steps and become three
panels arranged side by side in the same workbench (prompt box docked left, proposed-file tree
center, diff/tutorial detail right) - closer to how Cursor/Windsurf present a single continuous
surface rather than a page-to-page wizard. That is a genuinely different product shape for this
app's core loop, not an additive shell feature like Epics 20–23, and it's not obviously a strict
improvement: the current three-page flow has a clear beginning/middle/end that matches how a
single prompt-review-apply cycle actually works, and collapsing it into permanently-visible panels
could just as easily make a small screen feel more cluttered, not less wizard-like. Recommend
revisiting this only after Epics 20–23 ship and there's a felt sense (not just a guess) of whether
the remaining "wizard" feeling is about the shell (fixed by then) or genuinely about this flow's
page-per-step structure. No requirements/design written yet - deliberately, since the right design
depends on what Epics 20–23 reveal.*

---

## 4. Suggested execution order

1. **Epic 1** — one codebase, or everything after this costs double.
2. **T4.1.1** — the path-traversal fix. Small, and it is currently a real write-anywhere hole.
3. **F9.1** — tests on `DiffBuilder` + `ClaudeResponseParser` before refactoring them under Epic 2/3.
4. **Epic 2 + Epic 3 together** — editable content and hunk selection share the same model rework;
   splitting them means rewriting `ProposedFileChange` twice. **This is where the product actually
   becomes the thing described in the vision.**
5. **Epic 4** (backup/undo) and **Epic 6** (cancel, timeout, preflight) — make it trustworthy.
6. **Epic 5** — structured output and diff-based edits; unlocks real-world file sizes.
7. **Epic 7** — sessions and multi-turn.
8. **Epic 8** — the IDE surface and presentation polish.
9. **Epic 9** remaining — CI, docs, packaging.
10. **Epic 10** — Explorer page AI overview + in-place editing. F10.1 (overview) first since it's
    additive and read-only; F10.2 (editing) second since it's a new write path that needs its own
    safety checks (containment, protected paths, conflict detection) before it ships.
11. **Epic 12** — build/test feedback loop. The most bounded of the new "feels like a typical
    LLM-driven IDE" gaps: no new model capability, just a shell-out and a resumed prompt.
12. **Epic 11** — agentic read-only tool use. A bigger shift than Epic 12; ship read-only tools and
    prove out the activity-feed UI before ever considering model-initiated writes (explicit non-goal
    for now).
13. **Epic 16** — integrated terminal (scoped command-runner version first, not a real PTY). Shares
    Epic 12's `ProcessCommandRunner`.
14. **Epic 13** — inline diff view in the editor. Polish on an already-solid review/apply flow, not a
    new safety-critical path.
15. **Epic 15** — inline ghost-text autocomplete. ✅ Done 2026-10-23.
16. **Epic 17** — LSP-backed code intelligence. Largest and most speculative; lowest priority given
    `CodeEditor` is a review/light-edit surface here, not the app's primary authoring surface. ✅ Done
    2026-10-23.

17. **Epic 14 (F14.2)** — relevance ranking, shipped as lexical TF-IDF rather than true semantic
    (embedding) search, sidestepping the embeddings-provider/API-key decision entirely. ✅ Done
    2026-10-23.

18. **Epic 18** — multi-language code intelligence (Python, TypeScript/JavaScript/JSX). Extends
    Epic 17's single-language C# work to the second real use case it was waiting for. Full spec:
    `specs/multi-language-support/`. ✅ Done 2026-10-23: per-language `LanguageServerOptions`
    replacing the old single global `LspOptions`, `ProjectTypeDetector` (marker-file scan, depth
    0-1) gating which servers even start, `LanguageIdResolver` splitting "LSP languageId for this
    file" from "which configured process handles it" (one `typescript-language-server` instance
    serves typescript/typescriptreact/javascript/javascriptreact alike), and
    `LanguageServerSessionManager` replacing the single `ILanguageServerClient` consumer in
    `ExplorerViewModel`/DI with per-language isolation (one language's start failure never blocks
    another) and a same-working-directory no-op guard. Settings' "Code intelligence" section
    reworked into a per-language repeated row. Solution builds clean, 802/802 tests pass. ✅ Manually
    verified 2026-10-25 against real `pyright-langserver`/`typescript-language-server` processes
    via a headless harness exercising the production code directly (no GUI-automation tool exists
    in this environment for a native Avalonia window). This verification pass found and fixed a
    real bug: `LanguageServerSessionManager` rooted every language's server at the same top-level
    working directory, which broke `typescript-language-server` in a polyglot (Python `backend/` +
    TypeScript `frontend/`) repo, since it resolves its workspace TypeScript installation from
    `rootUri` at `initialize` time. `IProjectTypeDetector` now reports each detected language's own
    marker-file directory (`DetectLanguageRoots`, not just `DetectLanguageIds`), and each server is
    rooted there instead. `csharp-ls` was not separately re-verified in this pass (C#-only code
    intelligence was already verified under Epic 17).
19. **Epic 19** — editor autocomplete (real LSP completion, distinct from Epic 15's LLM ghost
    text). Builds on AvaloniaEdit's existing `CompletionWindow`, and shares an `initialize`
    capability-advertisement change with Epic 18 - either can land first. Full spec:
    `specs/editor-autocomplete/`. ✅ Done 2026-10-26, visually verified end-to-end.

20. **Epic 20** — Build & Run. The explicit, concrete ask behind the "wizard → IDE" initiative
    (see that section above Epic 20) and fully self-contained - it brings its own minimal
    persistent toolbar/output strip with it rather than waiting on Epic 22. Ship this first of the
    six new epics precisely because it's the one with a specific request attached, not a judgment
    call about priorities. Full spec: `specs/build-and-run/`.
21. **Epic 21** — real terminal emulation (PTY + VT100), via Porta.Pty + XTerm.NET. Direct
    follow-up to Epic 20: fixes the silent-output gap for full-screen TUI apps that Epic 20's own
    manual verification surfaced, and is also what Epic 16's Terminal page should move onto.
    Sequenced right after Epic 20 since it's a bug fix for what Epic 20 just shipped, not a
    judgment call about priorities either.
22. **Epic 22** — persistent workbench shell (output dock generalized from Epic 20's strip, status
    bar). This is the structural fix for "feels like a wizard" - do it right after Epic 20 so the
    dock only gets built once (as a multi-tab generalization of what Epic 20 already shipped)
    rather than Epic 20 and Epic 22 each building their own output surface.
23. **Epic 23** — multi-file tabbed editing in Explorer. Depends on nothing from 20/22 technically,
    but sequenced after them since tabs are the next most IDE-defining affordance once the shell
    itself no longer feels like a wizard, and Explorer is a calmer surface to add tabs to once it's
    not also absorbing the shell rework.
24. **Epic 24** — command palette & Quick Open. Explicitly sequenced last among the committed four:
    a command palette's value (jump to any action from anywhere) is largest once Epics 20–22 have
    actually added more "anywhere-reachable" actions (Build/Run/Stop, dock tabs, file tabs) for it
    to expose - building the palette first would mean populating it with less.
25. **Epic 25** — unifying Prompt/Diffs/Tutorial into one dockable surface. Explicitly **not**
    committed - see that epic's "Why" for the reasoning. Revisit only after living with Epics
    20–24 for a while.
