# Design — Build & Run

Companion to `requirements.md` in this directory. Implements R1–R6.

## Summary of the architectural change

Today: `IBuildTestRunner`/`ITerminalCommandRunner` (both `ProcessCommandRunner`) only know how to
run a command and wait for it to exit. Nothing persists across a page navigation except settings.

Target: a new per-working-directory list of `RunConfiguration`s (detected + hand-edited, same
provider seam as `LanguageServerOptions`), a new `IRunProcessManager` that can start a process and
hand back control immediately instead of waiting for it to exit, and a thin persistent strip added
to `ShellView` — one `RunPanelViewModel` singleton the whole app shares, not a page.

## New/changed types

### `Paarde.Core/Models/RunConfiguration.cs` (new)

```csharp
public record RunConfiguration(
    string Id,                      // Guid.ToString() — stable identity independent of Name/paths, so a
                                     // rename or path change doesn't break "which one was selected" (R1.2)
                                     // or R2.3's "don't clobber an edited entry" check.
    string Name,
    string LanguageId,              // same vocabulary as LanguageServerOptions.LanguageId
    string WorkingDirectoryPath,    // absolute; the project/script's own directory (see requirements.md's
                                     // open-question resolution), not necessarily the repo root
    string ProjectRelativePath,     // relative to WorkingDirectoryPath; "" if not file-based (e.g. a bare
                                     // script invocation with no distinct project file)
    string BuildCommand,            // "" = no build step (R3.3)
    string RunCommand,
    bool IsAutoDetected);           // R2.3/R2.6: false once the user edits or hand-creates it — detection
                                     // only ever touches entries still flagged true
```

### `Paarde.Core/Contracts/Services/IRunConfigurationDetector.cs` (new)

```csharp
public interface IRunConfigurationDetector
{
    // Pure detection, no persistence/merge logic — RunConfigurationsProvider (below) owns merging
    // this against what's already saved, the same split ProjectTypeDetector/LanguageServerOptions
    // already keep between "what's on disk" and "what's configured."
    IReadOnlyList<RunConfiguration> DetectConfigurations(IReadOnlyDictionary<string, string> languageRoots);
}
```

Takes `IProjectTypeDetector.DetectLanguageRoots`'s own return shape directly as input rather than
a raw working-directory path — detection re-uses Epic 18's language-root scan instead of walking
the filesystem a second time from scratch.

### `Paarde.Core/Services/RunConfigurationDetector.cs` (new)

- **C# (R2.1/R2.2):** for each `"csharp"` root, `Directory.EnumerateFiles(root, "*.csproj",
  SearchOption.AllDirectories)`, pruning `bin`/`obj`/`.git` directories during the walk (same
  exclusion `FileTreeBuilder`'s gitignore-aware walk already applies, reused here rather than
  reinvented). Each `.csproj`'s raw XML is sniffed with a small regex/`Contains` check for
  `IsTestProject>true`, `Microsoft.NET.Test.Sdk`, `OutputType>Exe`, `OutputType>WinExe`, and
  `Sdk="Microsoft.NET.Sdk.Web"` — a full MSBuild evaluation is unnecessary for this classification
  and would pull in a heavy dependency this codebase doesn't otherwise have. `Name` defaults to the
  project file's own name without extension (e.g. `"Paarde.Avalonia"`).
- **Python (R2.4):** for each `"python"` root, checks (in order) for `manage.py`, `app.py`,
  `main.py` directly inside that root; the first match found becomes one candidate with
  `BuildCommand = ""`, `RunCommand = "python <file>"`. No recursive search — matches
  `ProjectTypeDetector`'s own depth-0/depth-1 scope rather than exceeding it.
- **TypeScript (R2.5):** for each `"typescript"` root, parses that root's `package.json` `scripts`
  object; `dev` (preferred) or `start` becomes `RunCommand = "npm run dev"`/`"npm start"`, `build`
  becomes `BuildCommand = "npm run build"`. One candidate per root (not one per script) — a repo
  with both is one runnable thing with an optional build step, mirroring how the C# case treats one
  project as one candidate.

Every candidate's `Id` is a fresh `Guid.ToString()`; merging that against a *previous* detection
pass's candidate for the same project is `RunConfigurationsProvider`'s job (below), identified by
`(LanguageId, ProjectRelativePath)`, not by `Id` — `Id` is stable only once a configuration is
actually persisted.

### `Paarde.Core/Contracts/Services/IRunProcessManager.cs` (new)

```csharp
public interface IRunProcessManager
{
    bool IsRunning { get; }

    // Fired on whatever thread the underlying Process.OutputDataReceived callback runs on — callers
    // marshal to the UI thread themselves, same requirement ITerminalCommandRunner's onOutputLine
    // already has.
    event Action<string>? OutputReceived;

    // null exitCode means the process was killed via Stop() rather than exiting on its own.
    event Action<int?>? Exited;

    // Returns once the process has started (R4.1) — does not wait for it to exit. Throws if the
    // process fails to start at all (bad executable, etc.) — same as Process.Start's own contract;
    // callers catch the same exception types ProcessCommandRunner already catches elsewhere
    // (IOException/UnauthorizedAccessException/Win32Exception).
    void Start(string command, string workingDirectoryPath);

    // No-op if nothing is running. Kills the full process tree (R4.4) — a shelled-out
    // "dotnet run"/"npm start" is a launcher, not the final process, on some platforms.
    void Stop();
}
```

Deliberately synchronous `Start`/`Stop` (not `Task`-returning) — unlike every other runner in this
codebase (`ClaudeCliRunner`, `ProcessCommandRunner`), this one's defining feature is that it does
**not** await completion; making `Start` `async Task` that still "returns once started" would be
misleading about what's actually being awaited (nothing — `Process.Start()` itself is synchronous).

### `Paarde.Core/Services/RunProcessManager.cs` (new)

Same `ProcessStartInfo` shelling pattern `ProcessCommandRunner` already uses (`cmd.exe /c` on
Windows, `/bin/sh -c` elsewhere; `RedirectStandardOutput`/`RedirectStandardError`;
`CreateNoWindow`), but:
- No `WaitForExitAsync` call in `Start` — it wires `OutputDataReceived`/`ErrorDataReceived` to
  raise `OutputReceived`, sets `EnableRaisingEvents = true`, wires the `Process.Exited` event to
  raise `Exited(process.ExitCode)` and clear `IsRunning`, calls `process.Start()` +
  `BeginOutputReadLine()`/`BeginErrorReadLine()`, and returns.
- `Stop()` calls `process.Kill(entireProcessTree: true)` if a process is running; `Process.Exited`
  still fires afterward (raising `Exited(null)` — a convention this class owns: it inspects whether
  `Stop()` was the cause via a private flag checked before reading `process.ExitCode`, since a
  killed process's exit code is platform-dependent and not meaningful here).
- Calling `Start` while already running stops the previous process first (R4.5), synchronously,
  before starting the new one.
- Registered as a DI **singleton** (`App.axaml.cs`) — one app-wide Run session, same lifetime
  reasoning Epic 17 gave for `ILanguageServerClient`/`ILanguageServerSessionManager`.
- `App.axaml.cs`'s existing `desktop.Exit` handler (which already calls
  `ILanguageServerSessionManager.StopAllAsync()` before `Environment.Exit(0)`) gains a
  `GetService<IRunProcessManager>().Stop()` call alongside it (R4.6) — same orphaned-process
  concern, same fix shape.

### `Paarde.Core/Models/RunConfigurationSettingsKeys.cs` / `Paarde.Avalonia/Services/RunConfigurationsProvider.cs` (new)

Same provider seam as `LanguageServerOptionsProvider`:

```csharp
public class RunConfigurationsProvider
{
    public async Task<IReadOnlyList<RunConfiguration>> GetConfigurationsAsync(string workingDirectoryPath)
    {
        var saved = await _localSettingsService.ReadSettingAsync<List<RunConfiguration>>(KeyFor(workingDirectoryPath)) ?? new();
        var detected = _detector.DetectConfigurations(_projectTypeDetector.DetectLanguageRoots(workingDirectoryPath));

        var merged = saved.ToList();
        foreach (var candidate in detected)
        {
            var existingIndex = merged.FindIndex(c => c.LanguageId == candidate.LanguageId && c.ProjectRelativePath == candidate.ProjectRelativePath);
            if (existingIndex < 0)
            {
                merged.Add(candidate); // R2.3: never-seen-before candidate, append
            }
            else if (merged[existingIndex].IsAutoDetected)
            {
                merged[existingIndex] = candidate with { Id = merged[existingIndex].Id, Name = merged[existingIndex].Name };
                // still auto-detected and the user hasn't renamed/edited it — refresh commands in
                // case the project file changed (e.g. OutputType flipped), but keep its Id/Name
                // stable so the persisted "selected configuration" pointer (below) still resolves.
            }
            // else: user has edited this one (IsAutoDetected == false) — left untouched (R2.3).
        }

        await SaveConfigurationsAsync(workingDirectoryPath, merged);
        return merged;
    }
}
```

Settings key is **per working directory** (`RunConfigurationSettingsKeys.ForWorkingDirectory(path)`,
same keyed-per-path convention `WorkingDirectorySettingsKeys`'s sibling settings already use
elsewhere in this codebase for per-project state) rather than one global list — a `RunConfiguration`
only makes sense in the working directory it was detected in.

`SelectedRunConfigurationId` is a second, separate per-working-directory setting (plain string),
read/written alongside the list.

### Relationship to Epic 12's `BuildTestSettingsKeys.BuildCommand`

Epic 12's global `BuildCommand` stays exactly as-is — a single, automatic, whole-repo check that
runs right after an LLM apply, independent of which `RunConfiguration` (if any) is selected here.
The two are allowed to diverge after one seed: the very first time `RunConfigurationDetector`
produces a C# candidate for a working directory that has no saved configurations yet *and* Epic
12's global `BuildCommand` setting is non-blank, that global value seeds the new candidate's
`BuildCommand` instead of the computed `dotnet build "<path>"` default — a user who already
customized Epic 12's build command (e.g. added `-warnaserror`) doesn't have to retype it. After
that one seed, edits to either setting never propagate to the other.

### `Paarde.Avalonia/ViewModels/RunPanelViewModel.cs` (new, DI singleton)

Holds `ObservableCollection<RunConfiguration> Configurations`, `SelectedConfiguration`,
`OutputText`, `IsBuilding`, `IsRunning`, `StatusMessage`. Loaded/refreshed whenever the shared
working directory changes — subscribes to the same settings-change notification
`ExplorerViewModel`/`TerminalViewModel` already re-read `WorkingDirectorySettingsKeys.
WorkingDirectory` from on `OnNavigatedTo`; since this view model is a singleton living outside the
page-navigation lifecycle, it instead re-reads on a lightweight poll-on-focus or (cleaner) a new
`IWorkingDirectoryChanged` event raised wherever the working directory is currently saved
(`PromptViewModel.BrowseForWorkingDirectoryCommand`, `MainViewModel.OpenRecentProjectCommand`) —
a small, explicit pub/sub addition rather than a timer.

Commands:
- `BuildCommand` (`[RelayCommand(CanExecute = nameof(CanBuild))]`): runs
  `IBuildTestRunner.RunAsync(SelectedConfiguration.BuildCommand, SelectedConfiguration.
  WorkingDirectoryPath)` (R3.1); no-ops with a status message if `BuildCommand` is blank (R3.3)
  rather than disabling the button outright, so Run's implicit build step (below) has one code path
  to call regardless.
- `RunCommand` (`[RelayCommand(CanExecute = nameof(CanRun))]`): if `SelectedConfiguration.
  BuildCommand` is non-blank, awaits the same build step first; on build failure, sets
  `StatusMessage` and returns without starting the run (R4.3). On success (or no build needed),
  calls `IRunProcessManager.Start(SelectedConfiguration.RunCommand, SelectedConfiguration.
  WorkingDirectoryPath)`, subscribes `OutputReceived`/`Exited` (unsubscribing any prior
  subscription first — R4.5's "stop the previous one" also means not double-subscribing), and sets
  `IsRunning = true`.
- `StopCommand`: calls `IRunProcessManager.Stop()`.
- Switching `SelectedConfiguration` persists `SelectedRunConfigurationId` immediately (same
  "save on every change" pattern every other settings-backed property in this codebase uses).

### Shell placement (R5)

`ShellView.axaml`'s root `UserControl` content changes from directly hosting `<ui:NavigationView>`
to a `Grid` with `RowDefinitions="Auto,*,Auto"`:
- Row 0: a new toolbar `Border` — a `ComboBox` bound to `RunPanelViewModel.Configurations`/
  `SelectedConfiguration`, then Build/Run/Stop `Button`s (`IsEnabled` on `CanBuild`/`CanRun`, Stop
  only `IsVisible` while `IsRunning`), then `StatusMessage`. `ShellViewModel` gains a
  `RunPanelViewModel` property (constructor-injected) purely so this row has something to bind to —
  `ShellViewModel` itself stays otherwise unchanged.
- Row 1: the existing `<ui:NavigationView>` (unchanged — every page under it is unaffected).
- Row 2: a collapsible output strip (an `Expander`, default `IsExpanded="False"` but forced open by
  `RunPanelViewModel` whenever a build starts, fails, or a run starts — same "auto-expand on
  something worth seeing" behavior T10.1.4's Explorer overview panel already established) showing
  `OutputText` in a read-only, monospace, auto-scrolling `TextBox` — visually and behaviorally the
  same control Terminal's own output box already is, just anchored below every page instead of
  being its own page. Epic 22 (`ROADMAP.md`) is the one that generalizes this single strip into a
  multi-tab dock (Build/Run/Problems/Terminal) — this epic ships only the Build/Run tab's worth.

No existing page's `.axaml` changes. `TerminalView`/`TerminalViewModel` are untouched by this
epic — they remain the general "type any one-shot command" surface; Run is specifically "start the
configured long-lived run command," a different enough job to justify not folding Terminal into
`RunPanelViewModel` here (Epic 22 is where the two UIs visually merge into one dock, once both
exist).

## Testing approach

- `RunConfigurationDetectorTests` (`Paarde.Core.Tests`): synthetic temp directories with `.csproj`
  content variants — `OutputType>Exe`, `Sdk="Microsoft.NET.Sdk.Web"` with no `OutputType`, a plain
  library (`OutputType` absent, SDK not `.Web`) excluded, `IsTestProject>true` excluded, a
  `*.Tests.csproj` filename excluded even without the MSBuild property present. A `package.json`
  with `dev`/`start`/`build` scripts producing the expected single candidate; one with neither `dev`
  nor `start` producing none. A `manage.py` present producing a Django-style candidate.
- `RunConfigurationsProviderTests`: a first-time detection with no saved list appends everything; a
  previously-saved, user-edited (`IsAutoDetected: false`) entry survives a second detection pass
  unchanged even though the detector would compute different commands for it; a never-before-seen
  detected candidate is appended alongside existing entries; Epic 12's global `BuildCommand` seeds
  a first-ever C# candidate's `BuildCommand` exactly once.
- `RunProcessManagerTests`: starting a trivial long-running, platform-safe command (same
  cross-platform "sleep"-equivalent trick `ProcessCommandRunnerTests`/`TerminalViewModelTests`
  already use) raises `OutputReceived` for its output and leaves `IsRunning == true` until it's
  stopped; `Stop()` kills it and raises `Exited(null)`; a command that exits on its own raises
  `Exited(<its real code>)` and clears `IsRunning`; starting a second command while the first is
  still running stops the first one first (its `Exited` still fires) before the second starts.
- `RunPanelViewModelTests`: a configured build command runs before Run and a build failure prevents
  Run from starting; a blank build command skips straight to Run; `StopCommand` stops an active run
  and the panel reflects `IsRunning = false` afterward; switching `SelectedConfiguration` persists
  the new selection via a fake settings service.
- Manual verification (R6.2): same "no X server in this sandbox" caveat as every other
  `ShellView`/`CodeEditor`-touching task in this codebase — confirm the toolbar renders, Build/Run/
  Stop actually drive a real `dotnet build`/`dotnet run` against this repo itself, and the output
  strip auto-expands and streams live.
