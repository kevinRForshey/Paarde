# Implementation tasks — Fluent Design cohesion pass

Each task references the requirement(s) it satisfies. Not started — no code written yet.

## F29.1 — Window backdrop

- [x] T1 `MainWindow.axaml`: `TransparencyLevelHint="Mica,AcrylicBlur,None"`,
  `Background="Transparent"`, extended client-area title bar. _(R1)_
  - Follow-up: the Mica/Acrylic backdrop is now an opt-in Settings toggle
    (`SettingsViewModel.IsTransparencyEnabled`, persisted via `AppearanceSettingsKeys
    .TransparencyEnabled`), **off by default** - not something that should change the window's
    look silently. `MainWindow.axaml` no longer hardcodes `TransparencyLevelHint`/`Background`;
    `MainWindow.ApplyTransparency(bool)` applies both live, called once at startup
    (`App.axaml.cs`, alongside theme init) and again whenever the Settings toggle changes. The
    extended client-area title bar attributes stay unconditional - that's chrome layout, not the
    backdrop material.

## F29.2 — Shared chrome

- [x] T2 `App.axaml`: new `Style Selector="Border.popup-chrome"`; update `ShellView.axaml`'s Quick
  Open/Command Palette and `ExplorerView.axaml`'s MRU switcher popups to use
  `Classes="popup-chrome"`, dropping their now-redundant inline `Background`/`BorderBrush`/
  `BorderThickness`/`Padding` attributes. Also applied to `ShellView.axaml`'s Go to Symbol popup
  (`SymbolQuickPickPopup`), not named in this task but identical inline chrome - leaving it out
  would have defeated the point of the consolidation. _(R2)_

## F29.3 — Toolbar button icon convention

- [x] T3 Audit every page's top-level toolbar row (`ShellView`, `ExplorerView` - already done per
  `[[editor-focus-layout]]` - `ReviewPanelControl`/`ReviewFileDetailControl`, `HistoryView`,
  `TerminalView`, `SettingsView`) for bare `Button Content="text"` primary-action buttons; convert
  each to the icon+label `StackPanel` shape. Resolve the `Symbol.Play` collision between Run and
  Terminal's nav icon. _(R3)_
  - `ShellView`: Build/Run/Stop toolbar row and its embedded Terminal-tab duplicate converted;
    Terminal's `NavigationViewItem` icon changed `Play` -> `Keyboard` to resolve the collision.
  - `ExplorerView`: one leftover bare `Refresh` button (not caught by the prior pass) converted.
  - `ReviewPanelControl`: "Send errors to Claude"/"Refine selected file" converted (everything
    else in its sidebar already used the icon+label shape).
  - `ReviewFileDetailControl`: "Select all"/"Select none"/"Invert selection" and the
    Previous/Next step nav buttons converted (dropping the unicode `◀`/`▶` glyphs for real
    `ChevronLeft`/`ChevronRight` icons). Its top row (Revert buttons) was already done. The
    per-step buttons inside the repeated `ItemsControl` template (Apply this change/Explain
    further/View in Diff/Ask) are not a toolbar - left as-is.
  - `HistoryView`: Fork/Re-run row converted.
  - `TerminalView`: Run/Cancel/Clear row converted.
  - `SettingsView`: has no top-level toolbar row (plain settings form) - its Delete/Add run
    configuration/Reset to default buttons are in-context actions inside specific settings
    sections, not a toolbar, and were left as bare text buttons.

## F29.4 — Brand color and corner radius

- [x] T4 `MainView.axaml`: `#4F46E5` → `{DynamicResource SystemAccentColor}`. _(R4)_
  Confirmed this pattern (a `Color` resource assigned straight to a `Border.Background`) is
  already in use in `ExplorerFileTabControl.axaml`'s pending-change banner, so it's consistent
  with the existing codebase convention, not a new one.
- [x] T5 `SettingsView.axaml`/`ExplorerFileTabControl.axaml`/`MainView.axaml`: replace literal
  `CornerRadius` values with `{DynamicResource ControlCornerRadius}`/`OverlayCornerRadius` where
  appropriate; record with a comment any value kept literal as a deliberate decorative exception.
  _(R5)_
  - `ExplorerFileTabControl.axaml`'s pending-change banner (`4` -> `ControlCornerRadius`).
  - `SettingsView.axaml`'s language-server and run-configuration card borders (`6` ->
    `OverlayCornerRadius`).
  - `MainView.axaml`'s 64x64 brand-mark badge (`14`) kept literal with an explanatory comment -
    it's a deliberately larger/rounder radius than either theme token, not a themed control.

## F29.5 — `InfoBar` status messages

- [x] T6 Audit pages with a plain-`TextBlock`-bound status message (`ExplorerView` confirmed;
  check `ShellView` and others during implementation). Add a `StatusSeverity`
  (`InfoBarSeverity`) property beside each existing `StatusMessage` property, set at the same
  call sites that already assign `StatusMessage`. Replace each `TextBlock` with `ui:InfoBar`. 
  _(R6)_
  - Covered ViewModels/views: `ExplorerViewModel` (`ExplorerView.axaml`), `RunPanelViewModel`
    and `TerminalViewModel` (both status lines in `ShellView.axaml`, plus `TerminalView.axaml`),
    `PromptViewModel` (`PromptView.axaml` and `ComposePanelControl.axaml`), and
    `OpenFileTabViewModel` (`ExplorerFileTabControl.axaml`). Each got a `StatusSeverity`
    (`InfoBarSeverity`) property set at every existing `StatusMessage =` call site, and its
    `TextBlock` replaced with `ui:InfoBar` (`Message`/`Severity`/`IsOpen` via
    `StringConverters.IsNotNullOrEmpty`, `IsClosable="False"`).
  - Deliberately excluded: `TutorialStepViewModel`'s per-item `StatusMessage` inside
    `ReviewFileDetailControl.axaml`'s repeated `ItemsControl.ItemTemplate` (not a page-level
    status line - same per-item exclusion reasoning as T3), and `TutorialViewModel`'s
    differently-named status properties (not `StatusMessage`, out of this task's literal scope).
- [x] T7 Extend each affected ViewModel's existing `StatusMessage`-asserting tests to also assert
  the paired `StatusSeverity` at the same call sites (no new standalone tests needed - same
  assertions, one more property). _(validates R6, R7)_
  - Added `Assert.Equal(InfoBarSeverity.<X>, sut.StatusSeverity)` alongside the matching
    `StatusMessage` assertion in `ExplorerViewModelTests.cs` (3 sites), `TerminalViewModelTests.cs`
    (1 site), `OpenFileTabViewModelTests.cs` (3 sites), and `PromptViewModelTests.cs` (12 sites).
  - `RunPanelViewModelTests.cs` has no existing `StatusMessage` assertions to extend, so nothing
    was added there, per the task's own scope (extend existing assertions, not add new ones).
  - Full suite verified green afterward: `dotnet test` - 436/436 passed.

## F29.6 — Verification

- [ ] T8 Manual verification (by the user, on their own machine - this sandbox cannot render
  Avalonia UI): window backdrop renders Mica/Acrylic on a supporting platform and falls back
  cleanly elsewhere; all three popups share identical chrome; toolbar buttons are consistently
  icon+label across every page; the `MainView` brand mark follows the accent color; corner radii
  stay consistent across theme changes; `InfoBar` severities render with the correct color/icon
  per message type. _(validates AC-1 through AC-6)_
