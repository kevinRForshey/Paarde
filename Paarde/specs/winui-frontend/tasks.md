# Implementation tasks — WinUI 3 front end (`Paarde.WinUI`)

Each task references the requirement(s) it satisfies. Both solutions must build with 0 warnings
and existing tests must keep their counts after every task.

## F31.0 — Shared composition

- [x] T0 Record baseline: build warnings/errors, test pass counts, and the Avalonia DI registration
  list (service, implementation, lifetime, key). _(R5.1)_
  Done 2026-11-09: 0 warnings/0 errors; `Paarde.Presentation.Tests` 402/403,
  `Paarde.Avalonia.Tests` 63/63, `Paarde.Core.Tests` 525/565 (same known environmental failures);
  69 `services.Add*/Configure` lines.
- [x] T1 Add `AddPaardePresentation()` to `Paarde.Presentation`, move the shared registrations out
  of `App.axaml.cs`, and move data-folder resolution to `ApplicationDataPaths.Resolve`. Diff the
  registration list against T0; build, test, launch Avalonia. _(R1.1–R1.4)_
  Done 2026-11-09: `Paarde.Presentation/ServiceCollectionExtensions.cs` (verbatim moves, same
  lifetimes/keys) + `Models/ApplicationDataPaths.cs`, now also used by `LocalSettingsService`
  (removing its own copy of the default folder). The sorted registration lines across
  `App.axaml.cs` + the extension are identical to T0. Build 0/0, test counts unchanged, app
  launches with no errors logged.

## F31.1 — Scaffold, services, shell, Settings

- [ ] T2 Create `Paarde.WinUI` (unpackaged, self-contained, x64/ARM64) and `Paarde.Windows.slnx`;
  record package versions and licenses in `design.md`. App launches to an empty window. _(R2.1–R2.4)_
- [ ] T3 Host + DI in `App`: Serilog (`winui-log-` prefix), `AddPaardePresentation()`,
  `LocalSettingsOptions` from `appsettings.json`. _(R1, R3.3)_
- [ ] T4 `WinUiDispatcher`, `ThemeSelectorService`, `WindowAppearanceService`, `DialogService`,
  three pickers. _(R3.1)_
- [ ] T5 `PageService`, `NavigationService` (+ `IFrameNavigationService`), `NavigationViewService`,
  `NavigationHelper`, `ShellViewModel`, `ShellView` with `NavigationView` (nav only; dock/status
  bar/palette come later). _(R3.1, R3.2)_
- [ ] T6 Converters + `SettingsView`. Manual check: theme, transparency, runner settings,
  pickers, InfoBars, settings shared with Avalonia. _(R4.1, R5.2)_

## F31.2 — Read-mostly pages

- [ ] T7 `MarkdownTextBlock`, `DiffWordSpanTextBlock`. _(R4.3)_
- [ ] T8 `MainView`, `HistoryView`, `ReviewView` + `ReviewPanelControl` +
  `ReviewFileDetailControl`, `DiffsDetailControl`. Manual check. _(R4.1, R5.2)_

## F31.3 — Prompt

- [ ] T9 `PromptView` + `ComposePanelControl` (incl. @-mention popup, elapsed timer, streaming
  output). Manual check: a full prompt run. _(R4.1, R5.2)_

## F31.4 — Terminal and workbench dock

- [ ] T10 `TerminalControl` on Win2D (port rendering + key translation). _(R4.2)_
- [ ] T11 `TerminalView`; `ShellView` workbench dock (Run panel, terminal tab), status bar.
  Manual check: interactive shell, a TUI app, Run-panel build. _(R4.1, R4.2, R5.2)_

## F31.5 — Palette and shortcuts

- [ ] T12 Command palette, Quick Open, symbol quick pick, and `ShellView` keyboard shortcuts as
  `KeyboardAccelerator`s. Manual check. _(R4.1, R5.2)_

## F31.6 — Editor spike (O1)

- [ ] T13 Timeboxed spike: Monaco/WebView2 first, WinUIEdit if needed, scored against R4.4's
  checklist; record the decision, package and license in `design.md`. _(R4.4, R2.4)_

## F31.7 — Explorer

- [ ] T14 WinUI `CodeEditor` on the chosen control, covering every R4.4 item. _(R4.4)_
- [ ] T15 `ExplorerView`, `ExplorerFileTabControl`, `BreadcrumbBar`, `AssistantPanelControl`,
  pane splitters. Manual check: open/edit/save, diagnostics, completion, ghost text, F12, inline
  diff accept/reject. _(R4.1, R4.4, R5.2)_

## F31.8 — Wrap-up

- [ ] T16 Full side-by-side parity pass against the Avalonia app; final build/test counts. _(R5)_
- [ ] T17 `README.md` (WinUI build/run, `Paarde.Windows.slnx`) and `ROADMAP.md` Epic 31 entry.
