namespace Paarde.Core.Contracts.Services;

// Epic 21 (specs/terminal-emulation R1): same "start now, don't wait for exit" shape
// IRunProcessManager already has, but PTY-backed - a process spawned through this sees a real
// terminal device (ConPTY/forkpty) instead of a plain redirected pipe, so full-screen TUI apps
// (Terminal.Gui, vim, htop) render and behave correctly instead of silently emitting unparsed
// escape codes (requirements.md's root-cause analysis). Output is raw bytes, not lines - a PTY
// merges stdout/stderr into one stream the same way a real terminal does, and VT100/ANSI escape
// sequences aren't line-oriented to begin with.
public interface IPtyProcessManager
{
    bool IsRunning { get; }

    // Raw bytes as they arrive from the PTY. Callers feed these into a terminal-emulation buffer
    // (TerminalBuffer, specs/terminal-emulation T2) rather than splitting on newlines.
    event Action<byte[]>? OutputReceived;

    // Fires when the process terminates on its own (exit code) or null when it was terminated via
    // Stop() - same convention IRunProcessManager.Exited already uses, for the same reason (a
    // killed process's exit code is platform-dependent and not meaningful here).
    event Action<int?>? Exited;

    // cols/rows size the PTY itself at spawn time - a terminal device has a size from the moment
    // it's created, unlike a plain pipe. Starting while a previous run is still active stops it
    // first (same R4.5-equivalent semantics IRunProcessManager.Start already has).
    Task StartAsync(string command, string workingDirectoryPath, int cols, int rows, CancellationToken cancellationToken = default);

    // No-op if nothing is running.
    void Resize(int cols, int rows);

    // Writes bytes directly to the PTY's input side - callers are responsible for encoding
    // keystrokes into whatever byte sequence a real terminal would send (specs/terminal-emulation
    // R4), this just delivers them. No-op if nothing is running.
    void WriteInput(byte[] input);

    // No-op if nothing is running. Kills the full process tree - same reasoning
    // IRunProcessManager.Stop already documents (a shelled-out launcher can leave the real child
    // process behind otherwise).
    void Stop();
}
