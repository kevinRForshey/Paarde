# Implementation tasks — Real terminal emulation (PTY + VT100)

Each task references the requirement(s) it satisfies.

- [x] T1 Add the `Porta.Pty` package reference to `Paarde.Core`. Add `IPtyProcessManager`/
  `PtyProcessManager` (`Paarde.Core/Contracts/Services/`, `Paarde.Core/Services/`) per design.md:
  async `StartAsync`, raw-byte `OutputReceived`, `Exited`, `Resize`, `WriteInput`, `Stop`, with the
  same process-tree-kill and "stopped by us" semantics `RunProcessManager` already has. Register as
  a DI singleton; wire `Stop()` into `App.axaml.cs`'s existing exit handler alongside
  `IRunProcessManager`/`ILanguageServerSessionManager`. _(R1.1, R1.2, R1.3)_
  _(Done 2026-10-27: `Paarde.Core/Contracts/Services/IPtyProcessManager.cs`,
  `Paarde.Core/Services/PtyProcessManager.cs`. Verified against the real `TODOTUI1` repro from
  requirements.md's context section with a throwaway harness: the PTY-backed path now streams the
  app's alternate-screen-buffer redraw live, byte-for-byte, where the old pipe-based
  `RunProcessManager` produced nothing until teardown. Full 515/515 Core + 340/340 Avalonia suites
  still pass.)_
- [x] T2 Add the `XTerm.NET` package reference. Add `TerminalBuffer` (`Paarde.Core/Services/`) per
  design.md: wraps an `XTerm.NET` `Terminal`, exposes `Feed`/`Resize`/`GetRow`/`CursorPosition`/
  `Changed`, with no VT100/ANSI parsing logic of its own beyond what the library already does.
  _(R2.1, R2.2, R2.3)_
  _(Done 2026-10-27: `Paarde.Core/Services/TerminalBuffer.cs`. `Changed` is raised by
  `TerminalBuffer` itself after every `Feed`/`Resize`, not from XTerm.NET's own `BufferChanged` -
  that event only fires on alternate/normal screen switches (confirmed against the library's
  source), not on every write, so it isn't a general repaint signal. Color resolution
  (`ResolveColor`) was verified empirically against the real library rather than taken from its
  README, whose documented color-mode contract turned out not to match the source: mode 0 is a
  0-255 palette index (covering both basic 16-color and 256-color SGR, plus the 256/257 sentinel
  "default" values), mode 1 is direct packed RGB - there is no mode 2. `TerminalBufferTests`
  (`Paarde.Core.Tests`) covers plain text, cursor positioning, all three color modes, alternate-
  buffer round-tripping, resize, the `Changed` event, post-`Dispose` writes, and a regression test
  that feeds the exact byte-for-byte escape sequence captured live from the `TODOTUI1` repro and
  asserts it decodes into readable text. Full 526/526 Core + 340/340 Avalonia suites pass.)_
- [x] T3 Add `TerminalControl` (`Paarde.Avalonia/Controls/`): custom-drawn Avalonia control
  rendering a `TerminalBuffer`'s rows + cursor, subscribing to `Changed` and marshaling repaints via
  `Dispatcher.UIThread.Post`. _(R3.1, R3.2, R3.3)_
  _(Done 2026-10-27: `Paarde.Avalonia/Controls/TerminalControl.cs`. A plain `Control` subclass (no
  `.axaml`/template, the first standalone custom-drawn `Control` in this codebase - existing
  precedent like `BracketHighlightRenderer`/`DiagnosticsRenderer` are `IBackgroundRenderer`s tied to
  AvaloniaEdit's `TextView`, not this) with a bindable `Buffer` `StyledProperty<TerminalBuffer?>`
  following `CodeEditor`'s registration pattern. Subscribes/unsubscribes `TerminalBuffer.Changed` on
  property change via `AddClassHandler`, repainting through `Dispatcher.UIThread.Post(InvalidateVisual)`
  - the same UI-thread marshaling every other background-to-UI callback in this codebase uses.
  `Render` draws each cell with `FormattedText`/`Typeface("Cascadia Mono,Consolas,monospace")`,
  honoring per-cell foreground/background/bold/inverse/invisible from T2's `TerminalCell`, plus a
  cursor rectangle (filled when focused, outlined when not, per design.md's "standard terminal
  convention"). Cell size is measured once from a probe `FormattedText`. All `FormattedText`/
  `DrawingContext`/`Typeface` call shapes were confirmed by reflecting against the actual installed
  Avalonia 11.3.22 DLLs rather than guessed (e.g. the `IPen`-only `DrawRectangle` overload requires
  an explicit corner-radius `Single` argument not obvious from IntelliSense alone). Verified with a
  throwaway headless-rendering harness (`Avalonia.Headless`, version-pinned to match 11.3.22) that
  constructs a real `TerminalControl`, feeds it a `TerminalBuffer` fed with the actual captured
  `TODOTUI1` byte blob, and renders it to a `RenderTargetBitmap` with no exceptions - input handling
  (T4), resize-driven `cols`/`rows` recomputation (T5), and ViewModel/View wiring (T6/T7) are
  explicitly out of scope here per design.md. Full 526/526 Core + 340/340 Avalonia suites still
  pass.)_
- [x] T4 `TerminalControl` input handling: `OnTextInput`/`OnKeyDown` encode typed characters and
  special keys (arrows, Home/End, Page Up/Down, Ctrl+<letter>) into the byte sequences a real
  terminal sends, written via `IPtyProcessManager.WriteInput`, active only while a process is
  running. _(R4.1, R4.2)_
  _(Done 2026-10-27: `TerminalControl.OnTextInput`/`OnKeyDown` encode input and raise a new
  `InputReceived` event (`Action<byte[]>?`) rather than the control taking a direct dependency on
  `IPtyProcessManager` - the same "stay ignorant of the service, raise an event" shape `CodeEditor`
  already uses. The byte shape deliberately matches `IPtyProcessManager.WriteInput(byte[])`
  exactly so a consumer wires `control.InputReceived += ptyProcessManager.WriteInput;` directly;
  `WriteInput` already no-ops with no process running, so R4.2 falls out of that wiring rather than
  needing its own check here. The special-key encoding (arrows, Home/End/Page Up/Down, Insert/
  Delete, Enter/Tab/Backspace/Escape, Ctrl+A-Z) lives in a pure, `internal` `TryEncodeSpecialKey`
  method, directly unit-tested in `TerminalControlTests` (`Paarde.Avalonia.Tests`, 19 cases) without
  needing an Avalonia display - exactly what design.md's "Testing approach" called for. Key.A-Z
  contiguity and `KeyModifiers`/`Key` enum member names were confirmed by reflecting against the
  real installed Avalonia 11.3.22 assembly, not assumed. Full 526/526 Core + 359/359 Avalonia
  suites pass.)_
- [x] T5 `TerminalControl` resize handling: recompute `cols`/`rows` from pixel size and the
  monospace font's cell measurement on `OnSizeChanged`; call both `TerminalBuffer.Resize` and
  `IPtyProcessManager.Resize`. _(R1.3)_
  _(Done 2026-10-27: `TerminalControl.OnSizeChanged` recomputes the grid via a pure, `internal`
  `ComputeGridSize(Size pixelSize, Size cellSize)` method (unit-tested in `TerminalControlTests`:
  exact-multiple, partial-trailing-cell truncation, and smaller-than-one-cell clamping), calls
  `Buffer.Resize` directly since the control already holds that reference, and raises a new
  `Resized` event (`Action<int, int>?`) for the PTY side - same "stay ignorant of the service,
  raise an event" shape as T4's `InputReceived`, with the signature matching
  `IPtyProcessManager.Resize(int, int)` exactly so a consumer wires
  `control.Resized += ptyProcessManager.Resize;` directly. Guards against re-resizing when the
  computed grid already matches the buffer's current size (e.g. a layout pass that doesn't change
  pixel dimensions). Verified end-to-end with a throwaway headless harness: arranging a real
  `TerminalControl` at 1600x1000px resized its bound `TerminalBuffer` from 80x24 to 160x96 and
  raised `Resized(160, 96)`; re-arranging at the same size did not re-raise it. Full 526/526 Core +
  362/362 Avalonia suites pass.)_
- [x] T6 Wire Run (Epic 20): `RunPanelViewModel` owns a `TerminalBuffer`, starts processes through
  `IPtyProcessManager` instead of `IRunProcessManager`, and `ShellView.axaml`'s output strip hosts a
  `TerminalControl` bound to it instead of the current `TextBox`. No other part of the strip (the
  `Expander`, auto-expand behavior) changes. _(R3.3, R5.1; design.md "Shell/page placement")_
  _(Done 2026-10-27: `RunPanelViewModel` now takes `IPtyProcessManager` instead of
  `IRunProcessManager` and exposes `public TerminalBuffer OutputBuffer { get; }` in place of the old
  `OutputText` string property; `RunAsync` starts via `await _ptyProcessManager.StartAsync(...,
  OutputBuffer.Cols, OutputBuffer.Rows)`, `Stop()`/`WriteInput(byte[])`/`Resize(int,int)` passthrough
  to it, and `OnProcessOutputReceived(byte[])` feeds `OutputBuffer` directly instead of appending
  lines to a string. `ShellView.axaml`'s output-strip `TextBox` is replaced by
  `<controls:TerminalControl Name="OutputTerminal" Buffer="{Binding RunPanelViewModel.OutputBuffer}"
  />` inside the unchanged `Expander` (same `Grid.Row`/`Margin`/`Header`/`IsExpanded` binding/
  `MaxHeight`); `ShellView.axaml.cs` wires `OutputTerminal.InputReceived +=
  ViewModel.RunPanelViewModel.WriteInput;` / `.Resized += ViewModel.RunPanelViewModel.Resize;` in
  code-behind, following the same View-wires-to-ViewModel-passthroughs convention
  `ExplorerView.axaml.cs` already uses for `CodeEditor`'s events - the View never reaches past the
  ViewModel to the raw `IPtyProcessManager`.

  A critical VT100 subtlety was found and confirmed empirically with a throwaway harness before
  writing the fix: XTerm.NET's `Terminal` treats bare `\n` strictly per VT100 semantics (move down a
  row, don't return to column 0), producing a "staircase" render when fed `IBuildTestRunner`'s
  plain-text (non-PTY) output, which only ever contains bare `\n`. A new `EncodeForTerminal` helper
  (`text.Replace("\r\n", "\n").Replace("\n", "\r\n")`, UTF8-encoded) normalizes Build's output before
  feeding it into the same `OutputBuffer`; `IPtyProcessManager.OutputReceived` bytes need no such
  normalization since a real PTY's line discipline already emits `\r\n`. `OutputText = string.Empty`
  (both at the start of `RunAsync` and `BuildInternalAsync`) is replaced by feeding a
  `\x1b[2J\x1b[H` clear-screen-and-home-cursor sequence into `OutputBuffer`, since a `TerminalBuffer`
  has no separate "clear" concept beyond the same escape sequence a real terminal would receive.

  Confirmed via grep that `RunPanelViewModel` was the only real consumer of `IRunProcessManager`
  (besides DI registration/exit-stop in `App.axaml.cs` and its own test fake/suite) - once it moved
  to `IPtyProcessManager`, `Paarde.Core/Contracts/Services/IRunProcessManager.cs`,
  `Paarde.Core/Services/RunProcessManager.cs`, `Paarde.Core.Tests/Services/RunProcessManagerTests.cs`,
  and `Paarde.Avalonia.Tests/Fakes/FakeRunProcessManager.cs` were genuinely dead code and deleted
  outright, along with their DI registration and exit-stop call in `App.axaml.cs` (now just
  `GetService<IPtyProcessManager>().Stop();`). A new `FakePtyProcessManager`
  (`Paarde.Avalonia.Tests/Fakes/`) replaces `FakeRunProcessManager` in the rewritten
  `RunPanelViewModelTests`, with `StartCalls` now a `(Command, WorkingDirectoryPath, Cols, Rows)`
  tuple matching `IPtyProcessManager.StartAsync`'s signature. Full 520/520 Core (526 minus the 6
  deleted `RunProcessManagerTests`) + 362/362 Avalonia suites pass; both the test projects and
  `Paarde.Avalonia` itself build with 0 warnings/errors.)_

  _(Follow-up fix 2026-10-27, reported as "the integrated terminal works, but it is far too small
  to read and more importantly to be useful": `TerminalControl` had no `MeasureOverride`. A bare
  `Control` with no visual children and no template reports a 0x0 desired size, and the output
  strip's `Expander` sits in an `Auto` grid row, so it measures its content against infinite
  height - the control asked for nothing, got arranged at **zero** height, and `ComputeGridSize`
  clamped the buffer to a single row. Measured with a headless harness replicating ShellView's real
  `Auto,*,Auto` grid + `Expander` structure: before, `terminal.DesiredSize = 0,0`,
  `terminal.Bounds = 1384x0`, buffer `138 cols x 1 row`; after, `1384x360` and `138 x 32 rows`.
  Fixed with `MeasureOverride` delegating to a new pure, `internal` `ComputeDesiredSize` (unit-
  tested: fills finite space, falls back to a natural 24-row height on infinite height, and to a
  natural 80x24 grid on both-infinite). It deliberately ignores `Buffer`'s live cols/rows - deriving
  desired size from a buffer that `OnSizeChanged` resizes from the arranged size would close a
  measure -> resize -> re-measure feedback loop.

  Two readability fixes alongside it. (1) The font chain was `Cascadia Mono,Consolas,monospace`, and
  **neither Cascadia Mono nor Consolas is installed on Linux** (confirmed via `fc-list`), leaving
  only the generic `monospace` alias between the grid and `Program.cs`'s `WithInterFont()` default -
  a resolve failure lands on **proportional** Inter, which destroys a fixed-cell grid since every
  glyph is drawn at a measured-from-"M" offset. Concrete families (`DejaVu Sans Mono`,
  `Liberation Mono`, `Noto Sans Mono` - the first confirmed present via `fc-list :spacing=100`) now
  sit ahead of the generic alias. (2) Hard-coded `FontSize = 13` became a `StyledProperty<double>`
  defaulting to 14 and set explicitly in `ShellView.axaml`, so legibility is tunable without a
  recompile; changing it recomputes the measured cell and invalidates measure + visual. The strip's
  `MaxHeight` went 220 -> 360, since 220 couldn't fit a usable grid even once measuring was fixed
  (~21-22 rows at real DejaVu Sans Mono 14px metrics; the harness's 32 reflects headless stub font
  metrics, not a real display).

  Also verified the collapsed-by-default case (R5.2): a collapsed `Expander` still arranges the
  terminal at full height, so `Resized` fires exactly once across collapse -> expand -> recollapse
  and a bogus 1-row geometry never reaches `IPtyProcessManager.Resize`. Known limitation, not built
  because nothing needs it yet: `FontSize` changing *after* layout updates the cell metrics but
  won't re-derive cols/rows unless the arranged size also changes, so a future runtime zoom control
  would need to re-sync the grid explicitly. Full 520/520 Core + 365/365 Avalonia suites pass.)_

  _(Follow-up fix 2026-10-27, reported as "in the todo sample app, when I would run it and try to
  type into the app running in the terminal, it did nothing": confirmed via grep that nothing in
  the codebase ever called `.Focus()` on `OutputTerminal`, so after clicking Run, keyboard focus
  stayed wherever it already was (the Run button, which ignores letter keys) unless the user
  happened to click into the output strip first - matching the report exactly. Ruled out a
  structural focus-stealing problem in the surrounding toolbar/`Expander` layout first, via a
  headless harness replicating the real layout and simulating click -> focus -> type with
  `window.MouseDown`/`KeyTextInput`; an early run of that harness looked like clicks into the
  terminal area didn't transfer focus, but the click point had been computed from
  `terminal.Bounds` directly, which is parent-relative, not window-relative - fixed with
  `terminal.TranslatePoint(new Point(0,0), window)`, after which the harness confirmed the
  mechanism itself works correctly.

  Fixed in `ShellView.axaml.cs` by subscribing to `RunPanelViewModel.PropertyChanged` and calling
  `OutputTerminal.Focus()` when `IsRunning` becomes `true` - the same View-reacts-to-ViewModel-
  PropertyChanged-for-a-View-only-concern shape `TerminalView.axaml.cs` already uses for its own
  caret-follows-output behavior. `IsRunning` is set synchronously on the UI thread from inside the
  `RelayCommand`-driven `RunAsync()`, so no `Dispatcher.UIThread.Post` marshaling is needed (unlike
  `OnProcessOutputReceived`, which fires from the background PTY-reading thread). Full 520/520 Core
  + 365/365 Avalonia suites still pass (no new unit test was added for this one - it's
  code-behind requiring full DI construction, same reason the T6 `InputReceived`/`Resized` wiring
  above it has no direct test either). Not exercised against a real display/window manager in this
  sandboxed environment - the click/focus/type mechanics were confirmed via headless harness and
  the fix follows an established, already-proven pattern, but a live click-to-run -> type round
  trip through a real PTY-backed TUI app is something only a real display can fully confirm.)_
- [ ] T7 Wire Terminal (Epic 16): same swap for `TerminalViewModel`/`TerminalView.axaml` — output
  `TextBox` replaced by a `TerminalControl`, command input box and history unchanged.
  `ITerminalCommandRunner`'s existing wait-for-exit overload (used elsewhere, if any) stays; only
  the streaming path moves onto `IPtyProcessManager`. _(R3.3, R5.1)_
- [ ] T8 Tests: `PtyProcessManagerTests`, `TerminalBufferTests` per design.md's "Testing approach" —
  include the regression case using the actual escape-sequence blob captured from the `TODOTUI1`
  repro that originally surfaced this bug. _(validates R1, R2)_
- [ ] T9 Manual verification: re-run the `TODOTUI1` repro end-to-end (Run now renders the TUI app
  instead of staying blank; keyboard input drives it; Stop still leaves no orphaned process), and
  confirm an ordinary `dotnet build`/`git status` through the new path shows no visible regression
  from today's plain-text output. _(validates R3, R4, R5, Goal 6)_
