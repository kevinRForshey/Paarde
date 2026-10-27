# Implementation tasks — Build & Run

Each task references the requirement(s) it satisfies.

- [x] T1 Add `RunConfiguration` record, `RunConfigurationSettingsKeys` (per-working-directory keyed,
  plus a separate `SelectedRunConfigurationId` key), and `IRunConfigurationDetector`/
  `RunConfigurationDetector` (C# `.csproj` scan + classification, Python entry-point heuristic,
  TypeScript `package.json` scripts) + unit tests. _(R1.1, R1.3, R2.1, R2.2, R2.4, R2.5)_
  _(Done 2026-10-26: `Paarde.Core/Models/RunConfiguration.cs`,
  `Paarde.Avalonia/Services/RunConfigurationSettingsKeys.cs`,
  `Paarde.Core/Services/RunConfigurationDetector.cs`.)_
- [x] T2 Add `RunConfigurationsProvider` (`Paarde.Avalonia/Services/`, same seam as
  `LanguageServerOptionsProvider`): merges fresh detection against the saved list without
  clobbering a user-edited (`IsAutoDetected: false`) entry, and seeds a first-ever C# candidate's
  `BuildCommand` from Epic 12's global `BuildTestSettingsKeys.BuildCommand` when non-blank. _(R1.2,
  R1.3, R2.3, R2.6; design.md "Relationship to Epic 12")_
  _(Done 2026-10-26: `Paarde.Avalonia/Services/RunConfigurationsProvider.cs`. R2.6's hand-management
  UI lives in `SettingsViewModel`/`SettingsView.axaml`'s new "Run configurations" section, mirroring
  the existing "Code intelligence" rows.)_
- [x] T3 Add `IRunProcessManager`/`RunProcessManager`: `Start`/`Stop`, `OutputReceived`/`Exited`
  events, process-tree kill on `Stop`, auto-stop-the-previous-run on a second `Start`. Register as
  a DI singleton; wire `Stop()` into `App.axaml.cs`'s existing `desktop.Exit` handler alongside
  `ILanguageServerSessionManager.StopAllAsync()`. _(R4.1, R4.2, R4.4, R4.5, R4.6)_
  _(Done 2026-10-26: `Paarde.Core/Services/RunProcessManager.cs`; each running process tracks its
  own `StoppedByUs` flag so a second `Start()` can't misreport the prior process's exit.)_
- [x] T4 Add `RunPanelViewModel` (DI singleton): `Configurations`/`SelectedConfiguration` loaded via
  `RunConfigurationsProvider`, `BuildCommand`/`RunCommand`/`StopCommand`, Build-before-Run
  sequencing with failure short-circuit, blank-build-command skip. Add the small
  working-directory-changed pub/sub hook (design.md) so this singleton re-loads configurations
  when the shared working directory changes without needing `INavigationAware`. _(R3.1, R3.2, R3.3,
  R4.1, R4.3, R4.5)_
  _(Done 2026-10-26: `Paarde.Avalonia/ViewModels/RunPanelViewModel.cs`; the pub/sub hook is
  `IWorkingDirectoryContext`, fed by `PromptViewModel.OnWorkingDirectoryPathChanged`, with an
  eager load of the saved working directory in the constructor so the toolbar also works in a
  session that never visits the Prompt page.)_
- [x] T5 `ShellView.axaml`/`ShellViewModel`: add the toolbar row (configuration picker, Build/Run/
  Stop buttons, status text) and the collapsible output strip, per design.md's "Shell placement" -
  no other page's `.axaml` changes. _(R5.1, R5.2)_
  _(Done 2026-10-26.)_
- [x] T6 Tests: `RunConfigurationDetectorTests`, `RunConfigurationsProviderTests`,
  `RunProcessManagerTests`, `RunPanelViewModelTests` per design.md's "Testing approach". _(validates
  R1–R4)_
  _(Done 2026-10-26: 22 tests in `Paarde.Core.Tests`, 21 in `Paarde.Avalonia.Tests`; full suites pass
  (515 + 340). `RunPanelViewModelTests` doesn't exercise `OnProcessOutputReceived`/`OnProcessExited`
  since those marshal through `Dispatcher.UIThread.Post`, which this plain xunit host doesn't pump -
  same gap already documented on `TerminalViewModelTests`.)_
- [ ] T7 Manual verification against this repo itself: Build runs `dotnet build`, Run starts the
  Avalonia app (or a trivial console/web target) and streams live output, Stop actually terminates
  it with no orphaned `dotnet`/child process left behind (checked via `ps`/Task Manager), switching
  the configuration picker switches which project Build/Run target, and all of the above is usable
  from every page, not just one. _(validates R4.4, R4.6, R5.1, R5.2, R6.2)_
