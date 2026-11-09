# Implementation tasks — Presentation extraction (`Paarde.Presentation`)

Each task references the requirement(s) it satisfies. The build must be green and all tests must
pass after every task.

## F30.0 — Baseline

- [x] T0.1 Record baseline: `dotnet build Paarde.slnx` warnings/errors and pass counts for
  `Paarde.Core.Tests` and `Paarde.Avalonia.Tests`. _(R6.3)_
  Done 2026-11-08: 0 warnings/0 errors. `Paarde.Avalonia.Tests` 459/460, `Paarde.Core.Tests`
  525/565. All 41 failures pre-exist and are environmental on Windows: 29
  `PlatformNotSupportedException` ("Unix file modes", fake CLI/LSP scripts), git temp-repo
  cleanup `UnauthorizedAccessException`, and
  `SecureStorageServiceTests.ReadAsync_TamperedCiphertext_ThrowsRatherThanReturningGarbage`.
- [x] T0.2 Confirm the leak inventory in `requirements.md` is still accurate. _(R3.1)_
- [x] T0.3 Confirm no persisted setting or file stores a `Paarde.Avalonia.*` type/page-key
  string. _(R5.3)_
  Done: none persisted. The only literals were `NavigationHelper.NavigateTo="Paarde.Avalonia.ViewModels.*"`
  in `ShellView.axaml` (updated in T13). The `"Paarde.Avalonia/ApplicationData"` settings folder
  name is a path, not a type name, and is deliberately unchanged.

## F30.1 — Scaffold projects

- [x] T1 Create `Paarde.Presentation` and add it to `Paarde.slnx`. _(R1.1, R1.2)_
- [x] T2 Add a `ProjectReference` from `Paarde.Avalonia` to `Paarde.Presentation`. _(R1.3)_
- [x] T3 Create `Paarde.Presentation.Tests` with the assembly-reference guard test
  (`PresentationAssemblyReferenceTests`, anchored on `PresentationAssemblyMarker`). _(R1.4, R6.2)_

## F30.2 — Fix leaks in place

New abstractions were added directly to `Paarde.Presentation` (possible once T2 landed) rather
than to `Paarde.Avalonia` first.

- [x] T4 `NotificationSeverity` replaces `InfoBarSeverity` (17 files);
  `NotificationSeverityToInfoBarSeverityConverter` (registered in `App.axaml`) applied to the 8
  XAML `Severity` bindings; enum-alignment + mapping tests added. _(R2.1)_
- [x] T5 `IUiDispatcher`/`IUiTimer` + `AvaloniaUiDispatcher` (singleton) injected into the 6 view
  models; `FakeUiDispatcher` queues posted work (`RunPending()`) to match the old "no UI loop, posts
  never run" test behavior. _(R2.2)_
- [x] T6 `AppTheme` replaces `ThemeVariant`; `SettingsViewModel.ThemeVariant` property renamed to
  `Theme`; `EnumToBooleanConverter`'s `ThemeVariant` branch removed. Persisted values unchanged
  ("Default"/"Light"/"Dark"). _(R2.3, R5.3)_
- [x] T7 `INavigationService` reduced to `CanGoBack`/`NavigateTo`/`GoBack`; Avalonia-only
  `IFrameNavigationService` carries `Frame` + `Navigated` (see design.md). _(R2.4)_
- [x] T8 `IWindowAppearanceService` + `WindowAppearanceService`, used by `SettingsViewModel`. _(R2.5)_
- [x] T9 Verified: exactly the 70 files in the move map have no Avalonia dependency, direct or
  transitive. _(R3.1)_

## F30.3 — Move code (`git mv` + namespace edits)

- [x] T10–T13 Moved all 70 files in one pass (models, settings keys, strings, contracts, services,
  helpers, view models except `ShellViewModel`); namespaces `Paarde.Avalonia.*` →
  `Paarde.Presentation.*`; `using`s added/pruned across `Paarde.Avalonia`; `.axaml` `xmlns`
  retargeted (ShellView gets a second `pvm:` prefix since `ShellViewModel` stays);
  `Microsoft.Extensions.Options`, `Microsoft.Extensions.DependencyInjection.Abstractions` and
  `System.Security.Cryptography.ProtectedData` (moved from `Paarde.Avalonia`) referenced by
  `Paarde.Presentation`. _(R3.1, R3.2, R3.3, R4.3)_

## F30.4 — Tests

- [x] T15 Moved 26 test classes and all 31 fakes to `Paarde.Presentation.Tests`;
  `Paarde.Avalonia.Tests` keeps the 7 control/converter/Avalonia-helper tests and references
  `Paarde.Presentation.Tests` for the shared fakes. _(R6.1)_
- [x] T17 Final counts: `Paarde.Presentation.Tests` 402/403, `Paarde.Avalonia.Tests` 63/63,
  `Paarde.Core.Tests` 525/565 — 466 UI-side tests = baseline 465 + 1 guard test; only the same
  pre-existing failures. _(R6.3)_

## F30.5 — Verification and docs

- [ ] T18 Manual smoke test per `design.md`'s testing section (launch, theme, transparency,
  navigation incl. back, prompt run with elapsed ticker/streaming, Run panel build, terminal,
  InfoBars). _(R5.2)_
- [x] T19 `README.md` updated (tech table, project layout, test commands). `ROADMAP.md` entry still
  to add.
