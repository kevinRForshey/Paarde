namespace Paarde.Core.Contracts.Services;

// Epic 20 (specs/build-and-run R4): the one piece neither IBuildTestRunner nor
// ITerminalCommandRunner can represent - both of those are wait-for-exit (they return a
// CommandResult only once the process has already terminated), which is wrong for a `dotnet run`
// web server or any other long-lived process that's meant to keep running until the user stops
// it. Start is deliberately synchronous (not Task-returning) - unlike every other runner in this
// codebase, the point of this one is specifically *not* to await completion.
public interface IRunProcessManager
{
    bool IsRunning { get; }

    // Each line of stdout/stderr as it arrives, same shape as ITerminalCommandRunner's
    // onOutputLine callback.
    event Action<string>? OutputReceived;

    // Fires when the process terminates on its own (exit code) or null when it was terminated via
    // Stop() - a real exit code there would be meaningless (it's whatever killing the process tree
    // happened to produce) and would be indistinguishable from a coincidental matching exit code.
    event Action<int?>? Exited;

    // Starting while a previous run is still active stops it first (R4.5) rather than erroring or
    // leaving two processes running unknowingly.
    void Start(string command, string workingDirectoryPath);

    // No-op if nothing is running. Kills the full process tree (R4.4) - `dotnet run` is itself a
    // launcher for the actual app process on some platforms, so killing only the shelled-out
    // parent can leave the real one behind.
    void Stop();
}
