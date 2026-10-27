# Requirements — Real terminal emulation (PTY + VT100)

## Status

Draft. Not started. This is Epic 21 (`ROADMAP.md`) — a direct follow-up to Epic 20 (Build & Run),
not part of the broader "wizard → IDE" structural initiative that Epics 22–25 cover.

## Context and current state

Confirmed by direct inspection and reproduction, 2026-10-26:

- After Epic 20 shipped, a user reported that clicking **Run** against a Terminal.Gui-based
  console app (`/home/kevinf/Projects/testdotnet/TODOTUI1`) produced no visible output in the
  output panel, even though Build succeeded and the status text updated normally.
- Root cause, confirmed with a standalone repro harness against `RunProcessManager` directly:
  `Process.OutputDataReceived`/`BeginOutputReadLine` — used by both `RunProcessManager` (Epic 20)
  and `ProcessCommandRunner`/`ITerminalCommandRunner` (Epic 12/16) — is strictly line-oriented; it
  only fires when a `\n` byte arrives on the pipe. Full-screen TUI frameworks (Terminal.Gui, and
  anything else that uses the alternate screen buffer) redraw the entire screen via raw ANSI/VT100
  cursor-positioning escape sequences with no newlines in between (`\x1b[?1049h` to enter the
  alternate buffer, `\x1b[row;colH` to move the cursor, box-drawing characters, etc.). During
  normal operation this never fires `OutputDataReceived` at all; the only data ever observed
  arrives as a single blob when the pipe is torn down on `Stop()`, and even then it's unreadable
  raw escape-code soup, not rendered text.
- **There is no PTY (pseudo-terminal) anywhere in this codebase.** Without one, child processes
  see a plain anonymous pipe as their stdout, not a terminal device — many TUI libraries detect
  this (`isatty` returning false) and either refuse to run full-screen or, as observed here, run
  anyway and emit escape codes nobody can parse on the receiving end.
- This exact gap was already identified and explicitly deferred once before: Epic 16's T16.1.1
  (`ROADMAP.md`, 2026-10-22) considered a real PTY-backed terminal control and chose the
  scoped-down, line-only version instead, noting a true PTY was "meaningfully more work than every
  other control in this app." This epic is that deferred work, scoped to what Run/Terminal
  actually need rather than a general-purpose terminal emulator.
- Explicit standing instruction for this epic: build it with real, well-maintained libraries for
  the correctness-critical parts (PTY syscalls, ANSI parsing) rather than an improvised/partial
  implementation — "we're not pushing any LLM generated slop, we're doing this the right way."

## Goals

1. Any process Run (Epic 20) or Terminal (Epic 16) starts is given a real PTY, so full-screen TUI
   programs (Terminal.Gui, `vim`, `htop`, `top`, interactive REPLs that redraw in place) behave and
   render the same way they would in a real terminal emulator.
2. VT100/ANSI escape sequences (cursor movement, colors/attributes, alternate screen buffer,
   clear-line/clear-screen) are parsed into an actual screen state — text, position, styling — not
   displayed as raw escape codes or silently dropped.
3. That screen state is rendered live in Paarde's own UI (Run's output strip, Terminal's output
   area) as styled text in a fixed-size character grid, updating as the program redraws.
4. Keyboard input typed into that rendered surface is sent back to the child process through the
   PTY, including control characters and special keys (arrows, Ctrl+C, etc.) encoded the way a
   real terminal would send them, so interactive programs work both ways, not just output.
5. Resizing the rendering surface resizes the PTY (`SIGWINCH`-equivalent) so the child process
   reflows correctly, the same way resizing a real terminal window does.
6. Ordinary line-oriented commands (`dotnet build`, `git status`, `npm install`) keep working
   exactly as well as they do today — this must not regress Epic 20/16's existing behavior for the
   common, non-interactive case.

## Non-goals

- A general-purpose, feature-complete terminal emulator. No scrollback UI beyond whatever the
  underlying buffer gives for free, no Sixel/Kitty inline image graphics, no terminal
  window-manipulation escape sequences (resize-the-OS-window, title-bar changes, etc.), no tabs or
  split panes within the terminal surface itself — none of that is needed to make Run/Terminal
  behave correctly for TUI apps, which is the actual problem this epic fixes.
- Upgrading Avalonia to 12.x to adopt a ready-made terminal control
  (`Iciclecreek.Avalonia.Terminal`, which is built on the same two libraries this epic uses).
  Paarde is deliberately pinned to Avalonia 11.3.x (see `Paarde.Avalonia.csproj`); that upgrade
  would cascade into a major `FluentAvaloniaUI` bump and an `AvaloniaEdit` upgrade, an app-wide
  regression risk out of scope for a bug-fix epic. See design.md for the full tradeoff.
- A standalone "Terminal" page rewrite beyond swapping its renderer. Epic 16's page-level
  structure, command input box, and history are unchanged; only how output is captured and
  displayed changes.
- Writing a PTY implementation or a VT100 parser from scratch. Both are correctness-critical,
  well-trodden problems with mature, actively maintained libraries available — see design.md's
  "Library choice."

## Requirements

**R1 — PTY-backed process execution**
- R1.1 A process started for Run (Epic 20) or Terminal (Epic 16) is spawned with a real PTY
  (ConPTY on Windows, `forkpty`/`posix_openpt` on Linux/macOS) rather than a plain redirected pipe.
- R1.2 The PTY-backed path exposes the same lifecycle controls `RunProcessManager`/
  `ProcessCommandRunner` already have today: start, stop (kills the full process tree), and an
  exited notification with an exit code (or none, if stopped by the user).
- R1.3 The PTY can be resized (R Goals 5) without restarting the process.

**R2 — VT100/ANSI emulation**
- R2.1 Bytes read from the PTY are fed through a VT100/ANSI parser that maintains a screen-cell
  buffer: character, foreground/background color, bold/underline/italic attributes, and cursor
  position.
- R2.2 The parser supports the alternate screen buffer (entering/leaving it, e.g. Terminal.Gui's
  `\x1b[?1049h`/`\x1b[?1049l`), since that's exactly the mechanism TUI apps use to redraw a
  full-screen UI without scrolling the user's normal shell history.
- R2.3 256-color and true-color (24-bit) ANSI sequences are supported, not just the basic 16-color
  set, since modern TUI apps commonly use them.

**R3 — Rendering**
- R3.1 The screen-cell buffer (R2.1) is rendered as a fixed-size grid of monospace character cells
  with per-cell foreground/background color and basic attributes, plus a visible cursor at its
  current position.
- R3.2 The render updates live as the buffer changes — no "wait for the process to exit, then show
  the result" behavior, the same live-streaming expectation Epic 20/16 already set for the
  line-oriented case.
- R3.3 The rendering surface fits into both Run's existing collapsible output strip (Epic 20) and
  Terminal's existing output area (Epic 16) without changing either page's overall layout or
  navigation.

**R4 — Input**
- R4.1 Keystrokes typed while the rendering surface has focus are encoded as the bytes a real
  terminal would send (printable characters as themselves, arrow keys/Home/End/Page Up/Down as
  their VT100 escape sequences, Ctrl+<letter> as the corresponding control byte, etc.) and written
  to the PTY's input stream.
- R4.2 This applies only while a PTY-backed process is actively running; it has no effect on the
  command-input box Terminal already has for typing the command to run in the first place.

**R5 — Fallback for the non-interactive case**
- R5.1 Ordinary, non-interactive commands continue to work exactly as before — a PTY is still used
  (Goal 6 means this must not special-case "is this a TUI app," which isn't reliably knowable in
  advance), but plain line-oriented output renders as plain scrolling text through the same
  rendering surface, not visibly different from today's behavior for e.g. `dotnet build` output.

## Open questions (resolved in design.md)

- Whether to replace `RunProcessManager`/`ProcessCommandRunner` outright or add a parallel
  PTY-backed implementation selected at the call site — see design.md.
- Exact division of responsibility between the new rendering control and `XTerm.NET`'s own buffer
  model (how much of "which cell is what color" logic lives in Paarde code vs. the library).
