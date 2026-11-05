# Implementation tasks — Fluent Design cohesion pass

Each task references the requirement(s) it satisfies. Not started — no code written yet.

## F29.1 — Window backdrop

- [ ] T1 `MainWindow.axaml`: `TransparencyLevelHint="Mica,AcrylicBlur,None"`,
  `Background="Transparent"`, extended client-area title bar. _(R1)_

## F29.2 — Shared chrome

- [ ] T2 `App.axaml`: new `Style Selector="Border.popup-chrome"`; update `ShellView.axaml`'s Quick
  Open/Command Palette and `ExplorerView.axaml`'s MRU switcher popups to use
  `Classes="popup-chrome"`, dropping their now-redundant inline `Background`/`BorderBrush`/
  `BorderThickness`/`Padding` attributes. _(R2)_

## F29.3 — Toolbar button icon convention

- [ ] T3 Audit every page's top-level toolbar row (`ShellView`, `ExplorerView` - already done per
  `[[editor-focus-layout]]` - `ReviewPanelControl`/`ReviewFileDetailControl`, `HistoryView`,
  `TerminalView`, `SettingsView`) for bare `Button Content="text"` primary-action buttons; convert
  each to the icon+label `StackPanel` shape. Resolve the `Symbol.Play` collision between Run and
  Terminal's nav icon. _(R3)_

## F29.4 — Brand color and corner radius

- [ ] T4 `MainView.axaml`: `#4F46E5` → `{DynamicResource SystemAccentColor}`. _(R4)_
- [ ] T5 `SettingsView.axaml`/`ExplorerFileTabControl.axaml`/`MainView.axaml`: replace literal
  `CornerRadius` values with `{DynamicResource ControlCornerRadius}`/`OverlayCornerRadius` where
  appropriate; record with a comment any value kept literal as a deliberate decorative exception.
  _(R5)_

## F29.5 — `InfoBar` status messages

- [ ] T6 Audit pages with a plain-`TextBlock`-bound status message (`ExplorerView` confirmed;
  check `ShellView` and others during implementation). Add a `StatusSeverity`
  (`InfoBarSeverity`) property beside each existing `StatusMessage` property, set at the same
  call sites that already assign `StatusMessage`. Replace each `TextBlock` with `ui:InfoBar`. 
  _(R6)_
- [ ] T7 Extend each affected ViewModel's existing `StatusMessage`-asserting tests to also assert
  the paired `StatusSeverity` at the same call sites (no new standalone tests needed - same
  assertions, one more property). _(validates R6, R7)_

## F29.6 — Verification

- [ ] T8 Manual verification (by the user, on their own machine - this sandbox cannot render
  Avalonia UI): window backdrop renders Mica/Acrylic on a supporting platform and falls back
  cleanly elsewhere; all three popups share identical chrome; toolbar buttons are consistently
  icon+label across every page; the `MainView` brand mark follows the accent color; corner radii
  stay consistent across theme changes; `InfoBar` severities render with the correct color/icon
  per message type. _(validates AC-1 through AC-6)_
