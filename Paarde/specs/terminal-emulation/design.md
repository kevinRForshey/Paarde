# Design — Real terminal emulation (PTY + VT100)

Companion to `requirements.md` in this directory. Implements R1–R5.

## Library choice

Researched against the actual current (2026-10-26) state of the ecosystem — GitHub maintenance
signals (`pushed_at`, `archived`, license, stars/issues via `gh api repos/<owner>/<repo>`) and
exact published NuGet versions/dependency ranges (NuGet flat-container/registration APIs), not
guessed — per explicit instruction to do this "the right way," not with "LLM generated slop."

- **[Porta.Pty](https://www.nuget.org/packages/Porta.Pty)** (tomlm) — cross-platform PTY
  allocation: ConPTY on Windows, `forkpty`/`posix_openpt` on Linux/macOS. MIT, `net10.0`,
  NativeAOT-compatible, actively maintained (pushed the same day this research was done). Async
  API: `PtyProvider.SpawnAsync(PtyOptions, CancellationToken)` → `IPtyConnection` exposing
  `ReaderStream`, `WriterStream`, `Pid`, `ExitCode`, a `ProcessExited` event, `Resize(cols, rows)`,
  `Kill()`, `WaitForExit(ms)`.
- **[XTerm.NET](https://www.nuget.org/packages/XTerm.NET)** (tomlm, same author) — a headless,
  UI-framework-agnostic VT100/ANSI terminal-emulation engine ("bring your own renderer"). MIT,
  `net10.0`, actively maintained. Full VT100/ANSI support, 256/true color, Unicode/wcwidth-aware
  cell widths, normal + alternate screen buffer. Its `Terminal` class is `IDisposable` and exposes
  the parsed screen state for an external renderer to draw.

**Rejected/deprioritized alternatives:** `XtermSharp` (migueldeicaza) and `VtNetCore`
(darrenstarr) — both stale since 2022/2023, no recent maintenance activity. `AvaloniaTerminal`
(IvanJosipovic) — built on the stale `XtermSharp` engine. `ghostflyby/Pty.Net` — a one-star,
unproven fork. `MiniPty` — new, narrowly NativeAOT-focused, not a fit here.

**The one real tradeoff:** the same author's `Iciclecreek.Avalonia.Terminal` is a feature-complete,
drop-in Avalonia `TerminalControl`/`TerminalWindow` built directly on Porta.Pty + XTerm.NET — it
would otherwise be the obvious choice over building a control ourselves. It requires Avalonia
`[12.0.4, )`. Paarde is deliberately pinned to `11.3.22` (`Paarde.Avalonia.csproj`'s own comment:
"Pinned to the 11.3.x line rather than the 12.x major to avoid an untested breaking upgrade; 11.3.22
resolves the Tmds.DBus.Protocol high-severity advisory (NU1903) that 11.2.1 pulled in."). Adopting
it would cascade into a major `FluentAvaloniaUI` 2.5.1→3.1.0 bump (which itself depends on Avalonia
12.1.0) and an `Avalonia.AvaloniaEdit`/`AvaloniaEdit.TextMate` 11.4.0→12.0.0 upgrade — an app-wide,
high-regression-risk change with no relationship to the actual bug being fixed.

**Decision (confirmed by the user 2026-10-26):** stay on Avalonia 11.3.x. Consume Porta.Pty and
XTerm.NET directly — both are Avalonia-version-agnostic — and build a small, Paarde-owned,
Avalonia-11-compatible rendering control instead of adopting the third-party widget.

## Summary of the architectural change

Today: `RunProcessManager` (Epic 20) and `ProcessCommandRunner`/`ITerminalCommandRunner` (Epic
12/16) both shell a command through a plain `Process` with redirected pipes, and report output as
discrete lines via `OutputDataReceived`. That's structurally unable to represent a full-screen TUI
app's output, which arrives as newline-free escape sequences (requirements.md's root-cause
analysis).

Target: both of those call sites get their process spawned through a PTY instead of a plain pipe,
and instead of lines, the raw byte stream feeds a per-session VT100/ANSI terminal buffer
(XTerm.NET). A new Avalonia control renders that buffer live and turns keyboard input back into
bytes written to the PTY. Critically, this is **one path for everything**, not a TUI-detection
special case (requirements.md Goal 6, R5.1) — an ordinary `dotnet build` still produces a stream of
lines, which XTerm.NET's buffer renders as plain scrolling text indistinguishable from today's
`TextBox`-based output; a TUI app's alternate-screen redraws now render correctly too. One
rendering surface replaces the plain `TextBox` in both places.

`IBuildTestRunner` (Epic 12's automatic post-apply build/test gate) is **not** touched — nothing
watches its output live, and a PTY/terminal buffer would add overhead with no benefit there.

## New/changed types

### `Paarde.Core/Contracts/Services/IPtyProcessManager.cs` (new)

```csharp
public interface IPtyProcessManager
{
    bool IsRunning { get; }

    // Raw bytes as they arrive from the PTY (both stdout and stderr are merged into one stream by
    // the PTY itself, same as a real terminal - there's no separate stderr channel once a PTY is
    // involved). Callers feed these directly into a TerminalBuffer (below), not into any
    // line-splitting logic.
    event Action<byte[]>? OutputReceived;

    event Action<int?>? Exited;

    Task StartAsync(string command, string workingDirectoryPath, int cols, int rows, CancellationToken cancellationToken = default);

    void Resize(int cols, int rows);

    // Encodes typed input as the bytes a real terminal would send and writes them to the PTY.
    void WriteInput(byte[] input);

    void Stop();
}
```

Same shape as `IRunProcessManager` (Epic 20) otherwise — `StartAsync` is async only because
`PtyProvider.SpawnAsync` itself is (the underlying OS call is synchronous on some platforms but the
library's contract is `Task`-returning); it still returns once the process has *started*, not once
it exits.

### `Paarde.Core/Services/PtyProcessManager.cs` (new)

Wraps `Porta.Pty.PtyProvider.SpawnAsync` with a `PtyOptions` built from `command`/
`workingDirectoryPath`/`cols`/`rows` (same `cmd.exe`/`/bin/sh -c` wrapping `RunProcessManager`
already does, so PATH/shell builtins/`&&`-chains resolve the same way). A background read loop on
`IPtyConnection.ReaderStream` raises `OutputReceived` with whatever chunk `Stream.ReadAsync` hands
back (no line buffering — R1 is explicit that this is raw bytes). `IPtyConnection.ProcessExited`
raises `Exited`. `Stop()` calls `IPtyConnection.Kill()`. `WriteInput` writes to `WriterStream`.
Same "stopped by us" flag pattern `RunProcessManager` already uses, for the same reason (`Exited`
firing asynchronously during a racing second `Start`).

This **replaces** `RunProcessManager`'s and `ProcessCommandRunner`'s streaming-overload internals
rather than living alongside them — `IRunProcessManager`/`ITerminalCommandRunner`'s own interfaces
and call sites (`RunPanelViewModel`, `TerminalViewModel`) are unaffected; only what's inside them
changes, from a plain `Process` to a PTY. (`IBuildTestRunner`'s separate, still-`Process`-based,
wait-for-exit `RunAsync` is untouched, per "Summary" above.)

### `Paarde.Core/Services/TerminalBuffer.cs` (new)

Thin adapter around an `XTerm.NET` `Terminal` instance:

```csharp
public sealed class TerminalBuffer : IDisposable
{
    public TerminalBuffer(int cols, int rows) { /* constructs the XTerm.NET Terminal */ }

    // Fired after Feed() processes a chunk and the visible screen actually changed - the
    // rendering control subscribes to this instead of polling every frame.
    public event Action? Changed;

    public void Feed(byte[] bytes); // decodes + feeds into the XTerm.NET Terminal

    public void Resize(int cols, int rows);

    // One row's worth of cells (character, foreground, background, attributes) plus the current
    // cursor (row, col, visible) - exactly what the rendering control (below) needs and nothing
    // XTerm.NET-specific, so the control itself doesn't take a direct dependency on the library's
    // own types.
    public TerminalRow GetRow(int rowIndex);

    public (int Row, int Col, bool Visible) CursorPosition { get; }

    public int Cols { get; }
    public int Rows { get; }
}
```

This is where R2 (alternate screen buffer, 256/true color) is satisfied — entirely by XTerm.NET's
own `Terminal`; `TerminalBuffer` only adapts its output shape for the rendering control, it does
not reimplement any VT100/ANSI parsing itself (requirements.md's non-goal: not writing a parser
from scratch).

### `Paarde.Avalonia/Controls/TerminalControl.axaml.cs` (new)

A custom-drawn Avalonia control (overrides `Render(DrawingContext)` rather than a templated
control, the same way a character grid is typically drawn — this is not expressible as nested
`TextBlock`s at any reasonable performance). Takes a `TerminalBuffer` (constructor or a bindable
property) and:
- Subscribes to `TerminalBuffer.Changed`, calling `InvalidateVisual()` on the UI thread (same
  `Dispatcher.UIThread.Post` marshaling every other background-thread-to-UI callback in this
  codebase already needs).
- `Render` draws each visible row's cells as a sequence of `FormattedText`/glyph runs with their
  background rectangles, plus a filled or outline rectangle at `CursorPosition` (outline when the
  control doesn't have focus, filled/blinking when it does — standard terminal convention).
- `OnTextInput`/`OnKeyDown` overrides (R4) encode typed text and special keys (arrows, Home/End,
  Page Up/Down, Ctrl+<letter>) into the byte sequences a real terminal sends, and call
  `IPtyProcessManager.WriteInput` — only while a process is actually running (R4.2); otherwise
  input passes through untouched to whatever's behind it (e.g. Terminal's existing command box).
- `OnSizeChanged` recomputes `cols`/`rows` from the control's pixel size and the monospace font's
  measured character cell, and calls both `TerminalBuffer.Resize` and `IPtyProcessManager.Resize`
  (R1.3/Goal 5).

Scoped deliberately small: no scrollback beyond what XTerm.NET's own buffer provides for free, no
Sixel/Kitty image rendering, no selection/copy UI beyond what's trivial to add later if it turns
out to matter — none of that is required to fix the actual bug (requirements.md's non-goals).

## Shell/page placement

- **Run (Epic 20):** `ShellView.axaml`'s output strip's `TextBox` is replaced by a
  `TerminalControl` bound to a `TerminalBuffer` that `RunPanelViewModel` owns, fed by
  `IPtyProcessManager.OutputReceived` instead of today's `OutputReceived` string-line event. No
  other part of the strip (the `Expander`, its auto-expand-on-build/run behavior) changes.
- **Terminal (Epic 16):** `TerminalView.axaml`'s output `TextBox` is replaced the same way;
  `TerminalViewModel`'s command-input box and history are unchanged — only the output surface and
  the service underneath it change.

## Testing approach

- `PtyProcessManagerTests` (`Paarde.Core.Tests`): starting a trivial cross-platform command raises
  `OutputReceived` with real bytes and leaves `IsRunning == true` until stopped; `Stop()` kills it
  and raises `Exited(null)`; a command that exits on its own raises `Exited(<real code>)`;
  `Resize` doesn't throw while a process is running; `WriteInput` delivers bytes to a command that
  echoes stdin back out (verifies the round trip end-to-end without needing a real TUI app in CI).
- `TerminalBufferTests`: feeding known byte sequences — plain text with a trailing newline, an
  `\x1b[?1049h`/`\x1b[?1049l` alternate-buffer enter/exit, a cursor-position escape, a basic
  16-color and a 256-color SGR sequence — produces the expected `GetRow`/`CursorPosition` state.
  This is the regression test for the exact bug this epic fixes: feed the real escape-sequence
  blob captured from the `TODOTUI1` repro and assert it decodes into readable cell text rather than
  being dropped or left as literal escape bytes.
- `TerminalControl` rendering/input: like every other Avalonia control in this codebase, pixel-level
  rendering and real keyboard-event behavior need a display and are manual/deferred — but
  `OnKeyDown`'s byte-encoding logic (arrow keys → `\x1b[A`, etc.) is small and pure enough to unit
  test directly without an Avalonia test harness.
- Manual verification: re-run the exact `TODOTUI1` repro from requirements.md's context section and
  confirm the Run output strip now renders the app's UI instead of staying blank; confirm keyboard
  input (arrow keys, `q` to quit) actually drives it; confirm an ordinary `dotnet build` still
  renders as plain scrolling text with no visible regression.
