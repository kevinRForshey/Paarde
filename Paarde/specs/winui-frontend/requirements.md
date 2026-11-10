# Requirements — WinUI 3 front end (`Paarde.WinUI`)

## Status

Draft, 2026-11-09. Proposed as Epic 31 (`ROADMAP.md` entry still to be added). Depends on Epic 30
(`specs/presentation-extraction/`, done).

**Goal:** a Windows-only WinUI 3 desktop app, `Paarde.WinUI`, that reaches feature parity with
`Paarde.Avalonia` by reusing `Paarde.Presentation` + `Paarde.Core` unchanged and supplying only
views, controls and the UI-specific service implementations. Motivation: WinUI 3 / Windows App
SDK licensing is preferred over Avalonia's. `Paarde.Avalonia` keeps working throughout; retiring
it is a separate, later decision.

## Context and current state

Confirmed by direct inspection, 2026-11-09:

- `Paarde.Presentation` (net10.0, no UI references, guard-tested) holds 21 of 22 view models and
  every contract a front end has to satisfy. A front end must implement:

  | Contract (`Paarde.Presentation.Contracts.Services`) | Avalonia implementation today |
  | --- | --- |
  | `IUiDispatcher` / `IUiTimer` | `AvaloniaUiDispatcher` (`Dispatcher.UIThread`, `DispatcherTimer`) |
  | `INavigationService` | `NavigationService` (FluentAvalonia `Frame`, 197 lines) |
  | `IThemeSelectorService` | `ThemeSelectorService` (`AppTheme` → `ThemeVariant`, persists the name) |
  | `IWindowAppearanceService` | `WindowAppearanceService` → `MainWindow.ApplyTransparency` (Mica → Acrylic → none) |
  | `IDialogService` | `DialogService` (FluentAvalonia `ContentDialog`) |
  | `IFilePickerService`, `IFileSavePickerService`, `IFolderPickerService` | Avalonia `StorageProvider` |

  Avalonia-only pieces with no shared equivalent: `PageService` (7 page registrations),
  `NavigationViewService` + `ShellViewModel` (nav-pane selection sync), converters,
  `ResizablePaneHelper`, `NavigationHelper`.
- **DI composition is not shared.** `App.axaml.cs` (395 lines) registers ~50 Core/Presentation
  services, view models and options inline, plus Serilog and the application-data-folder
  resolution. A second host would duplicate all of it.
- **Views to port** (`Paarde.Avalonia/Views/`, ~2,900 lines `.axaml` + code-behind): `MainWindow`,
  `ShellView` (nav, command palette, quick open, workbench dock with Run panel and terminal, status
  bar, keyboard shortcuts), `MainView`, `PromptView`, `ComposePanelControl`, `HistoryView`,
  `ExplorerView`, `ExplorerFileTabControl`, `BreadcrumbBar`, `AssistantPanelControl`,
  `ReviewView`, `ReviewPanelControl`, `ReviewFileDetailControl`, `DiffsDetailControl`,
  `TerminalView`, `SettingsView`.
- **Custom controls** (`Paarde.Avalonia/Controls/`) and where they're used:

  | Control | Built on | Used by |
  | --- | --- | --- |
  | `CodeEditor` (687 lines) + `BracketHighlightRenderer`, `DiagnosticsRenderer`, `DiffBackgroundRenderer`, `DiffHunkGutterMargin`, `GhostTextRenderer`, `CodeEditorCompletionData` | AvaloniaEdit + TextMateSharp | `ExplorerFileTabControl` only |
  | `TerminalControl` (310 lines) | custom-drawn from Core's `TerminalBuffer` (XTerm.NET) | `TerminalView`, `ShellView` dock (3 places) |
  | `MarkdownTextBlock` (100 lines) | Avalonia inlines | `ReviewPanelControl`, `ReviewFileDetailControl`, `ExplorerFileTabControl` |
  | `DiffWordSpanTextBlock` (93 lines) | Avalonia inlines | `DiffsDetailControl` |

  The code editor is the only control with no straightforward WinUI equivalent, and it's confined
  to one view.
- Settings live under `%AppData%/Paarde/ApplicationData`, the `LocalSettingsOptions` default in
  `LocalSettingsService` and `appsettings.json`. They used to live under
  `Paarde.Avalonia/ApplicationData`, and are moved across on first start.
- `dotnet build Paarde.slnx` is the documented build and is expected to work on Linux/macOS for
  the Avalonia app. CI (`.github/workflows/ci.yml`) builds `Paarde.slnx` on Linux and
  `Paarde.Windows.slnx` on Windows, and runs the unit tests on both. It was broken from F31.0,
  when the second solution file made its bare `dotnet restore` ambiguous, until 2026-11-10.

## Requirements

### R1 — Shared composition

R1.1 Move the Core + Presentation service, view-model and options registrations out of
`App.axaml.cs` into an `IServiceCollection` extension in `Paarde.Presentation`
(`AddPaardePresentation()`), so both hosts register the shared graph identically.
R1.2 The extension MUST NOT register any UI-specific implementation (R2 contracts, views,
`PageService`, shell view model); each host registers those itself.
R1.3 Registration lifetimes are unchanged (singleton/transient/keyed exactly as today).
`Paarde.Avalonia` behavior is unchanged.
R1.4 The application-data-folder resolution (used by logging and `IRunSessionStore`) is shared, not
duplicated per host.

### R2 — New project

R2.1 Add `Paarde.WinUI`: WinUI 3 desktop app on the Windows App SDK, `net10.0-windows10.0.x`,
x64 + ARM64, referencing `Paarde.Presentation` (and `Paarde.Core` transitively).
R2.2 Unpackaged (`WindowsPackageType=None`) for this epic, so it runs with `dotnet run`/F5 and
uses the real `%AppData%` path (MSIX virtualizes it). MSIX packaging is out of scope.
R2.3 `Paarde.slnx` stays buildable on Linux/macOS: `Paarde.WinUI` goes in a separate Windows
solution (`Paarde.Windows.slnx`) that includes every project.
R2.4 Third-party packages MUST be MIT/Apache-2.0/similarly permissive (the reason for this epic).
The one exception is the platform itself: the Windows App SDK package ships under the Microsoft
Software License Terms (free, redistributable, Windows-only), not MIT. Each package added is
recorded with its license, as stated in the package's own license file, in `design.md`.

### R3 — UI-specific services

R3.1 Implement every contract in the table above for WinUI: `IUiDispatcher` on `DispatcherQueue`
/`DispatcherQueueTimer`; `INavigationService` on WinUI `Frame` with the same page-key contract
(`typeof(VM).FullName`, `INavigationAware`, `IConfirmNavigationAway`, `clearNavigation`);
`IThemeSelectorService` on `ElementTheme` with the same persisted values; `IWindowAppearanceService`
on `MicaBackdrop` → `DesktopAcrylicBackdrop` → none; `IDialogService` on `ContentDialog` (with
`XamlRoot` set, `Close` as the default button for confirms); pickers on
`Windows.Storage.Pickers` initialized with the main window handle.
R3.2 WinUI equivalents of `PageService`, `NavigationViewService` and `ShellViewModel`.
R3.3 Settings are shared with the Avalonia app: same `ApplicationDataFolder` default, same keys.
Running both apps against one settings folder is supported (not concurrently-safe; same as two
Avalonia instances today).

### R4 — Views and controls (parity)

R4.1 Every view in "Views to port" has a WinUI equivalent bound to the same Presentation view
model, with the same commands, keyboard shortcuts and InfoBar behavior. Visual parity is "same
Fluent layout and information", not pixel-identical.
R4.2 `TerminalControl` is re-implemented for WinUI, rendering Core's `TerminalBuffer` and wiring
`InputReceived`/`Resized` to `IPtyProcessManager` the same way.
R4.3 `MarkdownTextBlock` and `DiffWordSpanTextBlock` are re-implemented on WinUI
`RichTextBlock`/`TextBlock` inlines.
R4.4 The code editor supports what `CodeEditor` does today: syntax highlighting, two-way text
binding, bracket highlighting, LSP diagnostics squiggles, diff line backgrounds, diff-hunk gutter
with accept/reject, ghost-text suggestions, LSP completion list, go-to-definition (F12). The
control used is decided by a spike (see `design.md`, open question O1) before views that need it
are ported.
R4.5 `Paarde.Presentation` is not changed to accommodate WinUI except through R1 or a new
UI-agnostic abstraction that the Avalonia app also adopts. The Presentation guard test stays green.

### R5 — Verification

R5.1 Build: 0 warnings/0 errors for both solutions. Existing test counts unchanged (only the known
environmental failures).
R5.2 Each feature ends with a manual check of the ported pages against the Avalonia app.
R5.3 New automated tests only where logic lives outside XAML and isn't already covered by
`Paarde.Presentation.Tests` (expected: little or none).

## Out of scope

- MSIX packaging, Store submission, auto-update, code signing.
- Removing or freezing `Paarde.Avalonia`.
- New features or UX redesigns beyond what Avalonia has today.
- A full WinUI UI-automation test suite. A small FlaUI smoke suite was added later
  (`Paarde.WinUI.UiTests`; see tasks.md).
- Moving `ShellViewModel` into Presentation (each shell's nav sync is framework-specific).
