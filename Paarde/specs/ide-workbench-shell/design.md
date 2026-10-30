# Design — Persistent workbench shell

Companion to `requirements.md` in this directory. Implements R1–R4.

## Summary of the architectural change

Today `ShellView` has a 3-row grid: the Build/Run/Stop toolbar (row 0, Epic 20), the
`NavigationView` (row 1), and a single collapsible `Expander`/`TerminalControl` output strip (row
2, Epic 20/21). This epic only touches row 2 and adds a new row 3:

- Row 2 becomes a `TabControl` with four tabs (Build, Run, Problems, Terminal) inside the same
  `Expander`, instead of one `TerminalControl` directly inside it. One shared `IsExpanded` still
  covers the whole dock — **decision**: tabs do not get independent expand/collapse state. A user
  switching tabs expects to keep looking at the dock, not have it collapse; per-tab expand state
  would also double the persistence surface for no clear benefit. If this turns out to matter in
  practice it's a small follow-up, not a redesign.
- A new row 3 (`Grid.RowDefinitions="Auto,*,Auto,Auto"`) holds the status bar, anchored below
  everything.

## New/changed types

### `Paarde.Core/Contracts/Services/IDiagnosticsAggregator.cs` (new)

```csharp
public interface IDiagnosticsAggregator
{
    // Raised whenever the aggregated set changes (a file's diagnostics updated, or a file with
    // zero diagnostics was removed) - the Problems tab subscribes to this instead of polling.
    event Action? Changed;

    // Every file path with at least one outstanding diagnostic, as of the most recent publish
    // this instance has seen. Snapshot, not a live view - callers that need to react to changes
    // use Changed above.
    IReadOnlyDictionary<string, IReadOnlyList<LspDiagnostic>> Diagnostics { get; }
}
```

### `Paarde.Core/Services/DiagnosticsAggregator.cs` (new)

Subscribes to `ILanguageServerSessionManager.DiagnosticsPublished` directly in its constructor
(R4.2, R4.4) rather than being fed secondhand through `ExplorerViewModel` — this is what makes it
independent of Explorer's own lifecycle (R4.4): it's listening for the whole app session, not just
while Explorer happens to be the active page. On each publish: if the reported list is empty, the
file's entry is removed (R4.3); otherwise its entry is replaced wholesale (a publish is always a
full replacement of that file's diagnostics, same as `ExplorerViewModel.Diagnostics` already
assumes). Both branches raise `Changed`.

**Decision (closing-a-file doesn't clear diagnostics):** a file's entry is removed only by R4.3's
rule (the language server itself reports zero), never by the file being closed in Explorer. This
matches every mainstream IDE's Problems-panel convention — a compile error is a property of the
file/project, not of whether you currently have an editor open for it — and means the Problems tab
stays useful (shows every known problem across the repo) rather than shrinking to whatever happens
to still be open.

Registered as a DI singleton. Constructed early: `WorkbenchDockViewModel` (below) takes a
constructor dependency on it, and `WorkbenchDockViewModel` is itself constructed when
`ShellViewModel` is (eagerly, the same way `RunPanelViewModel` already is) — so it starts listening
before any page has navigated anywhere, same as `RunPanelViewModel`'s own eager construction
already guarantees for Build/Run.

### `ILanguageServerSessionManager` (changed)

Adds one read-only property:

```csharp
// R3.4: every LanguageId currently running a server - a snapshot of _clientsByServerGroup.Keys,
// not a live collection, so callers can't accidentally mutate session state through it.
IReadOnlyCollection<string> RunningLanguageIds { get; }
```

**Decision (what the status bar actually shows):** rather than a full configured-vs-running matrix
(which would need an extra async read of every language's `LanguageServerOptions` just to render a
status bar), the indicator is simply "`LSP: csharp, python`" (comma-joined `RunningLanguageIds`) or
"`LSP: none`" when empty. This answers the question the status bar actually needs to answer — "is
code intelligence alive right now" — without a second settings read on the UI thread.

### `GitStatus` (changed) / `IGitStatusService.GetStatus` (changed)

```csharp
public record GitStatus(bool IsRepository, string? BranchName, IReadOnlyList<string> DirtyFiles)
{
    public static readonly GitStatus NotARepository = new(false, null, Array.Empty<string>());
    public bool IsDirty => DirtyFiles.Count > 0;
}
```

`GitStatusService.GetStatus` runs a second git command, `git branch --show-current`, alongside the
existing `git status --porcelain` call, only once the directory is already confirmed to be a
repository. `--show-current` prints nothing (empty string) in detached-HEAD state; that maps to
`BranchName = null` rather than an empty string, so the status bar can render "(detached)"
explicitly instead of a blank. Same fail-open posture as the rest of the class: a failed branch
lookup never blocks the existing dirty-file detection, it just leaves `BranchName` null.

### `Paarde.Avalonia/Controls/CodeEditor.cs` (changed)

```csharp
public static readonly StyledProperty<LspPosition?> CaretPositionProperty =
    AvaloniaProperty.Register<CodeEditor, LspPosition?>(nameof(CaretPosition));

public LspPosition? CaretPosition
{
    get => GetValue(CaretPositionProperty);
    private set => SetValue(CaretPositionProperty, value);
}
```

Set from the same `TextArea.Caret.PositionChanged` subscription the constructor already has for
`_bracketHighlightRenderer` (R3.5) — one more statement in an existing handler, not a new
subscription. No new event is needed: `CaretPositionProperty.Changed` (an `AvaloniaProperty`
built-in) is exactly the "this changed" signal a host's code-behind needs, the same mechanism
`FilePathProperty.Changed`/`DiagnosticsProperty.Changed` etc. already use internally.

### `Paarde.Avalonia/Contracts/Services/IActiveEditorContext.cs` (new)

```csharp
// Mirrors IWorkingDirectoryContext's pub/sub shape exactly - a DI singleton that lets whichever
// page currently hosts a focused CodeEditor tell the status bar where the caret is, without the
// status bar (owned by ShellViewModel, a different page entirely) needing a reference to
// ExplorerView/TutorialDetailControl or vice versa.
public interface IActiveEditorContext
{
    event Action<LspPosition?>? Changed;

    // null means "no CodeEditor has focus right now" - the status bar hides the caret slot
    // entirely rather than showing a stale position (R3.5).
    void SetCaretPosition(LspPosition? position);
}
```

Each `CodeEditor` host (`ExplorerView.axaml.cs`, `TutorialDetailControl.axaml.cs`) wires, in its
constructor, next to its other `Editor.*` subscriptions:

```csharp
Editor.GotFocus += (_, _) => _activeEditorContext.SetCaretPosition(Editor.CaretPosition);
Editor.LostFocus += (_, _) => _activeEditorContext.SetCaretPosition(null);
Editor.GetObservable(CodeEditor.CaretPositionProperty)
    .Subscribe(position => { if (Editor.IsFocused) { _activeEditorContext.SetCaretPosition(position); } });
```

Only the currently-focused editor's changes are forwarded (the `IsFocused` guard) — with two
`CodeEditor` hosts potentially alive at once (Explorer and Tutorial are different pages, but both
instantiate their `View` once via DI and keep it around per the app's existing page-caching), a
caret move in the unfocused one must never overwrite what the status bar is showing for the
focused one.

### `Paarde.Avalonia/ViewModels/StatusBarViewModel.cs` (new)

A DI singleton (constructed as a property of `ShellViewModel`, same pattern as
`RunPanelViewModel`):

```csharp
public partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty] private string? gitBranchName;
    [ObservableProperty] private int dirtyFileCount;
    [ObservableProperty] private string runConfigurationName = "(none)";
    [ObservableProperty] private string languageServerStatus = "LSP: none";
    [ObservableProperty] private string? caretPositionText; // null -> status bar hides this slot
}
```

Fed by:
- `IWorkingDirectoryContext.Changed` + `RunPanelViewModel.PropertyChanged` (whenever
  `SelectedConfiguration` changes) → re-reads `IGitStatusService.GetStatus` for branch/dirty count
  and `RunConfiguration.Name` for the configuration slot. Git status is re-read on working-
  directory change and on a timer-free "whenever Explorer's apply flow stages/commits something"
  hook is explicitly **not** added here — see "Open question" below.
- `ILanguageServerSessionManager` — re-read on `DiagnosticsPublished` (a server being up enough to
  publish diagnostics is itself evidence of its running state changing) and once eagerly after
  `StartAllAsync` completes.
- `IActiveEditorContext.Changed` → formats `null` as `null` (R3.5's "shows nothing") or
  `$"Ln {position.Line + 1}, Col {position.Column + 1}"` (`LspPosition` is 0-based, status bars
  conventionally show 1-based).

### Dock: `Paarde.Avalonia/ViewModels/WorkbenchDockViewModel.cs` (new)

A DI singleton, constructed as a `ShellViewModel` property exactly like `RunPanelViewModel`:

```csharp
public partial class WorkbenchDockViewModel : ObservableObject
{
    public RunPanelViewModel RunPanelViewModel { get; } // already a singleton; reused, not duplicated
    public TerminalViewModel TerminalViewModel { get; } // promoted to a singleton - see below
    public IDiagnosticsAggregator DiagnosticsAggregator { get; }

    [ObservableProperty] private string activeTabKey = "Run"; // "Build" | "Run" | "Problems" | "Terminal"
    [ObservableProperty] private bool isExpanded;
}
```

Persists `ActiveTabKey`/`IsExpanded` via `ILocalSettingsService` using the same
restore-on-construct/save-on-change shape `ResizablePaneHelper` already established (R2.2), under
a new `WorkbenchDockSettingsKeys` (`Dock.ActiveTab`, `Dock.IsExpanded`) — not reusing
`WorkspaceLayoutSettingsKeys`, since those are per-page pane widths and this is a single app-wide
value, a different enough shape to warrant its own small key file (matching how
`RunConfigurationSettingsKeys` got its own file instead of folding into
`WorkingDirectorySettingsKeys`).

**R1.2 (separate Build/Run buffers):** `RunPanelViewModel` splits its single `OutputBuffer` into
`BuildOutputBuffer` and `RunOutputBuffer`. `BuildInternalAsync` feeds the former; `RunAsync`/
`OnProcessOutputReceived` feed the latter. `IsOutputExpanded`/the old auto-expand-on-build-or-run
behavior becomes `WorkbenchDockViewModel.IsExpanded = true` plus setting `ActiveTabKey` to
`"Build"` or `"Run"` respectively (R5.2-equivalent: starting an action should bring you to it,
same UX Epic 20 already established, now expressed as "expand the dock and switch to the relevant
tab" instead of "expand the one strip").

**Terminal placement (R1.3) — decision:** `TerminalViewModel`'s DI lifetime changes from
`AddTransient` to `AddSingleton` (mirroring `RunPanelViewModel`), and its keyed
`IPtyProcessManager` registration is unchanged (it already has its own, independent from Run's).
`TerminalView` (the page) and the dock's Terminal tab both bind to this one shared instance instead
of each getting their own. The `NavigationViewItem` **stays** (requirements.md's non-goals) as a
deep-link — navigating to it now shows the same live session the dock's Terminal tab shows,
rather than a second, independent one. This was the one real design choice flagged as "decide at
implementation time" in `ROADMAP.md`'s original Epic 22 text: a second independent instance was
rejected because two PTY sessions racing to both claim "the" Terminal tab's content would be
confusing with no corresponding user benefit (unlike Run's toolbar vs. a hypothetical second Run
view, there's no scenario where a user wants two different terminal command histories open at
once from the same shell).

**Problems tab (R1.4):** binds directly to `WorkbenchDockViewModel.DiagnosticsAggregator.
Diagnostics`, flattened into a `ListBox` of `(FilePath, Severity, Message, Line, Column)` rows —
a pure presentation flattening done in `ShellView.axaml.cs`'s code-behind reacting to
`DiagnosticsAggregator.Changed` (`Dispatcher.UIThread.Post`-marshaled, same as every other
background-thread-to-UI callback in this codebase), not a new ViewModel-level collection type.

## `ShellView.axaml` layout change

```xml
<Grid RowDefinitions="Auto,*,Auto,Auto">
    <!-- Row 0: existing Build/Run/Stop toolbar, unchanged -->
    <!-- Row 1: existing NavigationView, unchanged -->
    <Expander Grid.Row="2" Header="Output"
              IsExpanded="{Binding WorkbenchDockViewModel.IsExpanded}" MaxHeight="360">
        <TabControl SelectedItem="{Binding WorkbenchDockViewModel.ActiveTabKey}">
            <TabItem Header="Build">
                <controls:TerminalControl Buffer="{Binding WorkbenchDockViewModel.RunPanelViewModel.BuildOutputBuffer}" />
            </TabItem>
            <TabItem Header="Run">
                <controls:TerminalControl Name="OutputTerminal"
                                           Buffer="{Binding WorkbenchDockViewModel.RunPanelViewModel.RunOutputBuffer}" />
            </TabItem>
            <TabItem Header="Problems">
                <ListBox Name="ProblemsList" />
            </TabItem>
            <TabItem Header="Terminal">
                <!-- hosts the same content TerminalView.axaml already has, bound to the shared
                     WorkbenchDockViewModel.TerminalViewModel instance -->
            </TabItem>
        </TabControl>
    </Expander>
    <!-- Row 3: new status bar -->
    <Border Grid.Row="3" DataContext="{Binding StatusBarViewModel}">
        <StackPanel Orientation="Horizontal">
            <TextBlock Text="{Binding GitBranchName}" />
            <TextBlock Text="{Binding DirtyFileCount}" />
            <TextBlock Text="{Binding RunConfigurationName}" />
            <TextBlock Text="{Binding LanguageServerStatus}" />
            <TextBlock Text="{Binding CaretPositionText}" IsVisible="{Binding CaretPositionText, Converter={x:Static ObjectConverters.IsNotNull}}" />
        </StackPanel>
    </Border>
</Grid>
```

`TabControl.SelectedItem` binding to a string `ActiveTabKey` (rather than an index) needs each
`TabItem.Header` to equal one of `"Build"|"Run"|"Problems"|"Terminal"` and a
`FuncValueConverter`/manual code-behind sync if Avalonia's `TabControl` doesn't resolve a string
`SelectedItem` against `Header` directly — confirmed at implementation time against the installed
Avalonia 11.3.22 behavior; the fallback (code-behind sets `TabControl.SelectedIndex` from a small
`Dictionary<string,int>` keyed the same way `ActiveTabKey`'s four literal values are) is
straightforward if so.

## Testing approach

- `DiagnosticsAggregatorTests` (`Paarde.Core.Tests`): publishing diagnostics for a new file adds
  it; publishing an empty list for a known file removes it (R4.3); publishing for two different
  files keeps both independently; `Changed` fires exactly once per publish.
- `GitStatusServiceTests` extended: a real repo's `GetStatus` returns the actual current branch
  name (via a throwaway `git init`+`git checkout -b`/`git commit` fixture, same style the existing
  suite already uses); `NotARepository`'s `BranchName` is null.
- `LanguageServerSessionManagerTests` extended: `RunningLanguageIds` reflects exactly the languages
  successfully started, empty after `StopAllAsync`.
- `WorkbenchDockViewModelTests` (`Paarde.Avalonia.Tests`): `ActiveTabKey`/`IsExpanded` round-trip
  through a `FakeLocalSettingsService`, restored on construction, saved on change (same shape
  `ResizablePaneHelperTests` already covers for pane widths).
- `StatusBarViewModelTests`: branch/dirty-count/configuration-name/LSP-status update on their
  respective trigger events; caret slot is null until `IActiveEditorContext.Changed` fires, and
  goes back to null when it fires with `null`.
- `CodeEditor`/`ShellView` rendering, as with every other Avalonia control in this codebase, needs
  a real display and is manual/deferred.

## Open question

- Whether `StatusBarViewModel`'s git status should also refresh after an Explorer apply/stage
  operation (not just on working-directory change) so the dirty-file count reflects an apply
  immediately rather than on next navigation. **Leaning toward yes as a fast follow**, wired via
  the same `IWorkingDirectoryContext`-style pub/sub rather than a new dependency from
  `IFileChangeApplyService` straight into `StatusBarViewModel` — deferred out of this epic's first
  pass since it's an enhancement, not part of R3.2's baseline ask.
