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

- [x] T2 Create `Paarde.WinUI` (unpackaged, self-contained, x64/ARM64) and `Paarde.Windows.slnx`;
  record package versions and licenses in `design.md`. App launches to an empty window. _(R2.1–R2.4)_
  Done 2026-11-09: Windows App SDK 2.6.5. Its license is the Microsoft Software License Terms, not
  MIT (R2.4 updated to say so).
- [x] T3 Host + DI in `App`: Serilog (`winui-log-` prefix), `AddPaardePresentation()`,
  `LocalSettingsOptions` from `appsettings.json`. _(R1, R3.3)_
- [x] T4 `WinUiDispatcher`, `ThemeSelectorService`, `WindowAppearanceService`, `DialogService`,
  three pickers. _(R3.1)_
- [x] T5 `PageService`, `NavigationService` (+ `IFrameNavigationService`), `NavigationViewService`,
  `NavigationHelper`, `ShellViewModel`, `ShellView` with `NavigationView` (nav only; dock/status
  bar/palette come later). Unported pages are XAML placeholders. _(R3.1, R3.2)_
- [x] T6 Converters + `SettingsView`. Manual check: theme, transparency, runner settings,
  pickers, InfoBars, settings shared with Avalonia. _(R4.1, R5.2)_
  Done 2026-11-09, checked by driving the running app through UI Automation and screenshots:
  launch → Explorer placeholder with header; Settings shows the Avalonia app's saved values,
  including the Headroom error InfoBar; Light theme applies live (title bar too) and persists;
  Mica toggles live and persists; Back returns to Explorer; both settings reverted; closing
  the window exits cleanly; no WRN/ERR in `winui-log-*.txt`. **Not yet exercised:** the pickers and
  `DialogService` (Settings uses neither; first real use is in F31.2/F31.3), and the
  unsaved-changes navigation prompt.
  Both solutions build with 0 warnings/0 errors; test counts unchanged.

## F31.2 — Read-mostly pages

- [x] T7 `MarkdownTextBlock`, `DiffWordSpanTextBlock`. _(R4.3)_
  Done 2026-11-09. `SyntaxTokenizer` moved from `Paarde.Avalonia/Helpers` to
  `Paarde.Presentation/Helpers` (with its tests) so both front ends share it. This brings
  `TextMateSharp.Grammars` (MIT) into Presentation, so it was removed from the guard test's
  forbidden-prefix list. `DiffWordSpanTextBlock` uses `TextHighlighter` ranges for word
  highlights because WinUI's `Run` has no `Background`.
- [x] T8 `MainView`, `HistoryView`, `ReviewView` + `ReviewPanelControl` +
  `ReviewFileDetailControl`, `DiffsDetailControl`. Manual check. _(R4.1, R5.2)_
  Done 2026-11-09, checked through UI Automation and screenshots against a scratch data folder
  (`LocalSettingsOptions__ApplicationDataFolder`). Checked:
  - Home: recent runs and projects.
  - History: selecting a session and a file.
  - Diffs: syntax colours, word highlights, row tints, expanding collapsed regions, next/previous
    change, side-by-side with horizontal scroll, copy file contents.
  - Review: overview Markdown, tri-state file checkbox, Apply Selected (confirm dialog → file
    written → results dialog, status and Apply History), Undo Last Apply (confirm → file restored).

  This is the first real use of `DialogService`: dialogs pick up the app theme, and Cancel is the
  default button. Clean exit with no WRN/ERR in the log.
  The `GridSplitter` package (CommunityToolkit.WinUI.Controls.Sizers) came in here rather than in
  F31.7, for the Review file-list splitter.
  **Known gap:** Review's per-file detail follows `TutorialViewModel.Selected`, which is driven by
  Explorer's file tabs, so it can't be shown until F31.7.
  **Not yet exercised:** the pickers (Export) and the unsaved-changes prompt.
  Both solutions build with 0 warnings/0 errors. Test counts are unchanged: Presentation 407/408,
  Avalonia 58/58, Core 525/565 (pre-existing failures).

## F31.3 — Prompt

- [x] T9 `PromptView` + `ComposePanelControl` (incl. @-mention popup, elapsed timer, streaming
  output). Manual check: a full prompt run. _(R4.1, R5.2)_
  Done 2026-11-09. Avalonia's `PromptView` repeats `ComposePanelControl`'s whole body. In WinUI,
  `PromptView` just hosts `ComposePanelControl` with its own `PromptViewModel`, and the control
  will be reused by the Assistant panel in F31.7. WinUI-specific changes:
  - The mention keys use `PreviewKeyDown`, because an `AcceptsReturn` TextBox handles Enter/Up/Down
    before `KeyDown`.
  - `CaretIndex` → `SelectionStart`.
  - The mention `Popup` isn't light-dismiss, so the TextBox keeps keyboard input.
  - The Recent dropdown is reset from code-behind (WinUI's ComboBox ignores the view model clearing
    `SelectedItem` inside its own `SelectionChanged`).
  - Timeout is a `NumberBox` bound one-way and written back from `ValueChanged`, since its value is
    a double.

  Checked in the running app through UI Automation and real keystrokes:
  - Picking a recent project switches the working directory and resets the dropdown.
  - `@Gre` shows `Greeter.cs`; Enter inserts `@Greeter.cs ` with no newline, leaves the caret after
    it, and attaches the file; Escape closes the popup without changing the text.
  - A real Claude Code run against the scratch project: Cancel, spinner, ticking timer and
    streamed-character count while running; then a success InfoBar, the usage summary, "Continue
    previous conversation" enabled, and the new result on Review.

  Avalonia doesn't navigate to Review after a run either, so the Home page's "you'll be taken to
  the Review tab" wording is out of date in both apps.
  **Found (shared code, both apps, not fixed here):** `LocalSettingsService.SaveSettingAsync` writes
  `LocalSettings.json` from `Task.Run` with no lock. Switching the working directory triggered two
  saves at once, and one failed with an `IOException`, logged as a WRN by `RunPanelViewModel`.
  **Not yet exercised:** the pickers (Browse, Add files/folder) and Suggest relevant files.
  Both solutions build with 0 warnings/0 errors; test counts unchanged.

## F31.4 — Terminal and workbench dock

- [x] T10 `TerminalControl` ~~on Win2D~~ (port rendering + key translation). _(R4.2)_
  Done 2026-11-09, **without Win2D**: its NuGet package is under Microsoft's Win2D EULA rather
  than MIT, so the control draws with plain XAML elements and needs no new package (design.md
  "TerminalControl").
- [x] T11 `TerminalView`; `ShellView` workbench dock (Run panel, terminal tab), status bar.
  Manual check: interactive shell, a TUI app, Run-panel build. _(R4.1, R4.2, R5.2)_
  Done 2026-11-09. `ShellView` is now Avalonia's 4-row layout: the Build/Run/Stop toolbar and
  InfoBar, the NavigationView, the Output dock (`Expander` + `SelectorBar`), and the status bar.

  Checked with real keystrokes and screenshots:
  - **Terminal page with `cmd`:** prompt, `dir`, a coloured foreground/background line, `cls`,
    Up-arrow history, Backspace.
  - **Size:** `mode con` reports 134×22, matching the control, so `Resized` reaches the PTY.
  - **Dock:** expands; its Terminal tab shares the same session and takes focus.
  - **Build/Run** (the scratch project turned into a console app): the configuration was
    auto-detected, Build switched the dock to Build with the output and a success InfoBar, and the
    status bar showed it. Run switched to Run and focused the terminal; typed input reached the
    program, its green output rendered, and the exit code was reported. Stop reported "Stopped."
  - Rows snap to device pixels; before that fix, faint seams showed between rows.

  **Found (shared Core, both apps, not fixed here):**
  - `TerminalBuffer` never answers terminal queries (DA/DSR/OSC colour), so a TUI that waits for
    a reply (Microsoft `edit`) hangs on "…".
  - Ctrl+C reaches cmd (a new prompt appears), but a running `ping` isn't interrupted.
  - Cancel/Stop end only the top-level process: `edit.exe` (under `cmd`) and `Greeter.exe`
    (under `dotnet run`) were left running.

  **Not exercised:** the Problems tab with real diagnostics (needs a language server, F31.7).
  Both solutions build with 0 warnings/0 errors; test counts unchanged.

## F31.5 — Palette and shortcuts

- [x] T12 Command palette, Quick Open, symbol quick pick, and `ShellView` keyboard shortcuts as
  `KeyboardAccelerator`s. Manual check. _(R4.1, R5.2)_
  Done 2026-11-09. Ctrl+P / Ctrl+Shift+P / Ctrl+Shift+O are `KeyboardAccelerator`s on `ShellView`'s
  root (global scope, `KeyboardAcceleratorPlacementMode="Hidden"` so they don't show as a
  tooltip). WinUI-specific changes:
  - WinUI's `Popup` has no Center placement. The popups have a fixed width (480) and are centred
    horizontally from code-behind when they open, near the top of the page (like VS Code's) rather
    than Avalonia's vertical centre.
  - They're light-dismiss with `IsOpen` bound two-way, so a click outside also closes the view
    model.
  - Disabled palette entries are dimmed (`BindingFunctions.OpacityIfEnabled`), because WinUI's
    `TextBlock` has no `IsEnabled`.
  - The query boxes use `PreviewKeyDown` for Escape/Enter/Up/Down.

  Checked with real keystrokes, a mouse click and screenshots:
  - **Quick Open:** opens centred with the query focused and the first result selected; typing
    filters; Down moves the selection; Enter on `Greeter.cs` navigates to Explorer (still the
    placeholder).
  - **Command palette:** Ctrl+Shift+P from inside Quick Open swaps one popup for the other. The
    disabled Stop entry is dimmed, and "term" + Enter runs "Go to Terminal".
  - **Go to Symbol:** shows "No file is focused in the editor." with no editor open. Escape closes
    it.
  - **Light-dismiss:** a click outside closes the popup, and Ctrl+P reopens it.
  - **Focused terminal:** keeps Ctrl+P for the shell (no popup), same as Avalonia.
  - Clean exit; this run logged nothing at WRN or above.

  **Not exercised:** symbol results and jumping to one; both need Explorer's editor (F31.7).
  `Paarde.Windows.slnx` builds with 0 warnings/0 errors. Only `Paarde.WinUI` changed, so test counts
  are unchanged.

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
