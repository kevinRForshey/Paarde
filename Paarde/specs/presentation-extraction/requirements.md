# Requirements — Presentation extraction (`Paarde.Presentation`)

## Status

Done 2026-11-09, manually verified. Epic 30 in `ROADMAP.md`. Pure refactor: no
user-visible behavior change. Moves every piece of `Paarde.Avalonia` that does not need Avalonia
(view models, settings/navigation contracts, non-UI services, plain helpers/models) into a new
UI-framework-agnostic class library, `Paarde.Presentation`.

**Goal:** a future WinUI 3 Windows front end (`Paarde.WinUI`, not part of this epic) reuses
`Paarde.Presentation` + `Paarde.Core` unchanged and only supplies views and the UI-specific
service implementations. Every abstraction below is chosen so it maps 1:1 onto WinUI 3
(`InfoBarSeverity`, `ElementTheme`, `DispatcherQueue`, `Frame`).

## Context and current state

Confirmed by direct inspection, 2026-11-08 (after fast-forwarding `Development` to `0af4eae`):

- The solution has four projects: `Paarde.Core` (no UI; already owns `ProposedFileChangeViewModel`,
  `IPromptResultStore`, all LLM/LSP/PTY/build services), `Paarde.Avalonia` (WinExe: views,
  controls, view models, app services, DI composition root in `App.axaml.cs`), and their two test
  projects.
- `Paarde.Avalonia`'s non-view folders (`ViewModels/`, `Contracts/`, `Services/`, `Helpers/`,
  `Models/`, `Strings/`) contain ~90 `.cs` files. **Most have no `using Avalonia*`/`FluentAvalonia`
  at all**, but nearly all `ViewModels/` are *transitively* bound to Avalonia through a handful of
  leaks:

  | Leak | Where | Uses |
  | --- | --- | --- |
  | `FluentAvalonia.UI.Controls.InfoBarSeverity` | `ExplorerViewModel`, `OpenFileTabViewModel`, `PromptViewModel`, `RunPanelViewModel`, `SettingsViewModel`, `TerminalViewModel`, `IHeadroomCoordinator`, `IHeadroomStatusStore`, `HeadroomCoordinator`, `HeadroomStatusStore` | ~70 references; bound to `InfoBar.Severity` in 8 places across 7 `.axaml` files |
  | `Avalonia.Threading.Dispatcher.UIThread.Post` / `DispatcherTimer` | `ExplorerViewModel`, `PromptViewModel` (incl. elapsed-time timer), `QuickOpenViewModel`, `RunPanelViewModel`, `StatusBarViewModel`, `TerminalViewModel` | ~12 call sites |
  | `Avalonia.Styling.ThemeVariant` | `IThemeSelectorService`, `ThemeSelectorService`, `SettingsViewModel`, `EnumToBooleanConverter` | theme radio buttons |
  | `INavigationService` exposes `FluentAvalonia` `Frame` + `NavigatedEventHandler` | every VM that navigates (`AssistantPanel`, `CommandPalette`, `History`, `Main`, …) | `NavigateTo(string, object?, bool)`, `GoBack`, `CanGoBack`, `Navigated` |
  | `(App.MainWindow as MainWindow)?.ApplyTransparency(...)` | `SettingsViewModel` | transparency toggle (Epic 29) |
  | `NavigationEventArgs` / `INavigationViewService` (`NavigationView`, `NavigationViewItem`) | `ShellViewModel` | nav-pane selection sync |

- Because `ExplorerViewModel`, `PromptViewModel`, `RunPanelViewModel` and `TerminalViewModel` are
  bound, the otherwise-clean `AssistantPanelViewModel`, `CommandPaletteViewModel`,
  `HistoryViewModel`, `MainViewModel`, `TutorialViewModel`, `TutorialStepViewModel` and
  `WorkbenchDockViewModel` are bound too.
- `PageService` keys pages by `typeof(TViewModel).FullName`, and every `NavigateTo` caller passes
  `typeof(XViewModel).FullName!` — so a namespace change is transparent to navigation as long as no
  key is persisted as a literal string.
- `Paarde.Avalonia.Tests` already has 17 view-model test classes, 6 service test classes, 6 helper
  test classes and 28 fakes that do not touch Avalonia controls; only `Controls/` tests (4 files)
  need Avalonia. View-model code paths behind `Dispatcher.UIThread.Post` are currently awkward to
  test because there's no running dispatcher.

## Requirements

### R1 — New project

R1.1 Add `Paarde.Presentation` (`net10.0` class library, `RootNamespace` `Paarde.Presentation`)
to `Paarde.slnx`, referencing `Paarde.Core`.
R1.2 `Paarde.Presentation` MUST NOT reference any `Avalonia*`, `FluentAvalonia*`,
`AvaloniaEdit*` or `TextMateSharp*` package, directly or transitively.
R1.3 `Paarde.Avalonia` references `Paarde.Presentation`. Dependency direction is strictly
`Avalonia → Presentation → Core`.
R1.4 Add `Paarde.Presentation.Tests` (xUnit, same package versions as the existing test projects).

### R2 — UI-agnostic replacements for the leaks

R2.1 Replace `InfoBarSeverity` in Presentation code with a Presentation-owned enum
(`NotificationSeverity`: `Informational`, `Success`, `Warning`, `Error`). XAML bindings convert
it back to `InfoBarSeverity` in `Paarde.Avalonia`.
R2.2 Replace direct `Dispatcher`/`DispatcherTimer` use with an injected `IUiDispatcher`
abstraction (post to UI thread; create a repeating UI-thread timer). The Avalonia implementation
wraps `Dispatcher.UIThread`. Tests use a synchronous fake.
R2.3 Replace `ThemeVariant` in `IThemeSelectorService` and `SettingsViewModel` with a
Presentation-owned `AppTheme` enum (`Default`, `Light`, `Dark`). Only `ThemeSelectorService`
(Avalonia) maps it to `ThemeVariant`.
R2.4 Split navigation: a Presentation `INavigationService` with no `Frame` or FluentAvalonia
types (`CanGoBack`, `NavigateTo(string pageKey, object? parameter = null, bool clearNavigation =
false)`, `GoBack()`, and a `Navigated` event carrying the page key/view-model type and the
parameter). Frame wiring stays in Avalonia's `NavigationService`.
R2.5 Replace `App.MainWindow` access in `SettingsViewModel` with an injected window-appearance
abstraction (`IWindowAppearanceService.ApplyTransparency(bool)`).

### R3 — What moves

R3.1 Move to `Paarde.Presentation` every type in `Paarde.Avalonia`'s `ViewModels/`, `Contracts/`,
`Services/`, `Helpers/`, `Models/` and `Strings/` that is free of Avalonia after R2, except those
listed in R4. Namespaces change from `Paarde.Avalonia.*` to `Paarde.Presentation.*` (same
sub-folder names).
R3.2 Files are moved with `git mv` so history follows them.
R3.3 Interfaces whose implementation needs Avalonia (`IDialogService`, `IFilePickerService`,
`IFileSavePickerService`, `IFolderPickerService`, `IThemeSelectorService`, `IUiDispatcher`,
`IWindowAppearanceService`, `INavigationService`) move; their implementations stay.

### R4 — What stays in `Paarde.Avalonia`

R4.1 `App`, `Program`, `Views/`, `Controls/`, `Strings/*.resx` or other Avalonia resources, all
`IValueConverter`s, `ResizablePaneHelper`, `NavigationHelper`, `FrameExtensions`,
`TextEditorExtensions`, `SyntaxTokenizer` (TextMate grammar loading for the editor).
R4.2 Avalonia-backed implementations: `DialogService`, `FilePickerService`,
`FileSavePickerService`, `FolderPickerService`, `ThemeSelectorService`, `NavigationService`,
`NavigationViewService` (+ `INavigationViewService`), `PageService` (+ `IPageService`), plus the
new `IUiDispatcher`/`IWindowAppearanceService` implementations.
R4.3 `ShellViewModel` stays for this epic: it exists to synchronise FluentAvalonia's
`NavigationView` selection (`NavigationViewItem`, `SettingsItem`) and has no value without it.
Moving it is a possible follow-up, not in scope.
R4.4 `FileLoggerProvider` stays with the composition root (host logging wiring).

### R5 — Composition root and behavior

R5.1 `App.axaml.cs` remains the single DI composition root. Presentation registrations may be
grouped into an `IServiceCollection` extension in `Paarde.Presentation`
(`AddPaardePresentation()`) as long as lifetimes (singleton/transient/keyed) are unchanged.
R5.2 No user-visible behavior change: navigation, theme switching, transparency, InfoBars,
terminal/run output streaming and the prompt elapsed-time ticker behave as before.
R5.3 No persisted setting key or value format changes. If any setting stores a
`Paarde.Avalonia.*` type name, it is migrated or the change is rejected.

### R6 — Tests

R6.1 All tests for moved types move to `Paarde.Presentation.Tests` (with their fakes); `Controls/`
tests stay in `Paarde.Avalonia.Tests`.
R6.2 A guard test in `Paarde.Presentation.Tests` fails if the `Paarde.Presentation` assembly
references any assembly whose name starts with `Avalonia`, `FluentAvalonia`, `AvaloniaEdit` or
`TextMateSharp`.
R6.3 Total test count after the epic is ≥ the baseline count recorded before starting; all pass.

## Out of scope

- Creating the WinUI 3 project itself.
- New test coverage beyond keeping the existing tests passing (e.g. dispatcher-path tests).
- Moving `ShellViewModel`, any view, control, converter, or editor renderer.
- Splitting `Paarde.Core`, or moving anything between Core and Presentation.
- Behavior, UX, or styling changes.
- Upgrading Avalonia, FluentAvalonia, or CommunityToolkit.Mvvm.
