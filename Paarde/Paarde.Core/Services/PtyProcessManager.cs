using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Porta.Pty;

using Paarde.Core.Contracts.Services;

namespace Paarde.Core.Services;

// Same cmd.exe/sh -c shelling pattern RunProcessManager
// already uses (so PATH/shell builtins/&&-chains resolve the same way), but spawned through
// Porta.Pty so the child process gets a real PTY (ConPTY on Windows, forkpty/posix_openpt on
// Linux/macOS) instead of a plain redirected pipe - the fix for TUI apps rendering as unparsed
// escape codes (requirements.md's root-cause analysis).
public class PtyProcessManager : IPtyProcessManager
{
    // Same "travels with its own process, not a shared field" reasoning RunProcessManager's
    // RunningProcess.StoppedByUs already documents - ProcessExited fires asynchronously, so a
    // second StartAsync racing in could otherwise flip a shared flag out from under the old
    // connection's own handler.
    private sealed class RunningPty
    {
        public required IPtyConnection Connection { get; init; }

        public bool StoppedByUs { get; set; }

        private int _exitReported;

        // ProcessExited and StartAsync's already-exited check can both see the same exit.
        public bool TryMarkExitReported() => Interlocked.Exchange(ref _exitReported, 1) == 0;
    }

    private RunningPty? _current;
    private volatile bool _isRunning;

    public bool IsRunning => _isRunning;

    public event Action<byte[]>? OutputReceived;

    public event Action<int?>? Exited;

    public async Task StartAsync(string command, string workingDirectoryPath, int cols, int rows, CancellationToken cancellationToken = default)
    {
        // Same "starting a second run stops whatever's already active first" semantics
        // IRunProcessManager.Start already has.
        Stop();

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        if (isWindows)
        {
            // A process started in a new process group ignores Ctrl+C, and every process it starts
            // inherits that - so if this app was launched that way, Ctrl+C typed into the terminal
            // would reach cmd but not the program it runs. Clearing it here lets the children inherit
            // normal Ctrl+C handling.
            SetConsoleCtrlHandler(IntPtr.Zero, false);
        }
        var options = new PtyOptions
        {
            Cols = cols,
            Rows = rows,
            Cwd = workingDirectoryPath,
            App = isWindows ? "cmd.exe" : "/bin/sh",
            // Porta.Pty's default Windows formatting wraps every argument in quotes, which cmd.exe
            // misparses ('"exit 3' is not recognized...) - so on Windows the command line is passed
            // verbatim in the standard `cmd /s /c "<command>"` form, where /s strips exactly the
            // outer quotes and leaves any quotes inside the command itself untouched.
            CommandLine = isWindows ? ["/s", "/c", $"\"{command}\""] : ["-c", command],
            VerbatimCommandLine = isWindows,
        };

        var connection = await PtyProvider.SpawnAsync(options, cancellationToken);
        var entry = new RunningPty { Connection = connection };

        _current = entry;
        _isRunning = true;

        connection.ProcessExited += (_, e) => ReportExit(entry, e.ExitCode);

        // Porta.Pty starts watching for the exit inside SpawnAsync (on Linux/macOS, a thread that
        // raises ProcessExited the moment waitpid returns), so a quick command like `exit 3` can
        // exit - and raise the event with nobody subscribed - before the line above runs.
        if (connection.WaitForExit(0))
        {
            ReportExit(entry, connection.ExitCode);
        }

        _ = ReadLoopAsync(entry);
    }

    private void ReportExit(RunningPty entry, int exitCode)
    {
        if (!entry.TryMarkExitReported())
        {
            return;
        }

        // A run that Stop() replaced can finish exiting after the next one has started; it mustn't
        // mark the new run as stopped.
        if (ReferenceEquals(_current, entry))
        {
            _isRunning = false;
        }

        // Meaningless when Stop() caused the exit - same reasoning
        // RunProcessManager.Process.Exited already documents.
        Exited?.Invoke(entry.StoppedByUs ? null : exitCode);
    }

    private async Task ReadLoopAsync(RunningPty entry)
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                var bytesRead = await entry.Connection.ReaderStream.ReadAsync(buffer);
                if (bytesRead <= 0)
                {
                    // EOF - the connection closed its end, whether via natural exit or Kill().
                    break;
                }

                OutputReceived?.Invoke(buffer[..bytesRead]);
            }
        }
        catch (IOException)
        {
            // Reader stream torn down by Kill() - nothing left to read.
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            entry.Connection.Dispose();
        }
    }

    public void Resize(int cols, int rows) => _current?.Connection.Resize(cols, rows);

    public void WriteInput(byte[] input)
    {
        var connection = _current?.Connection;
        if (connection is null)
        {
            return;
        }

        connection.WriterStream.Write(input, 0, input.Length);
        connection.WriterStream.Flush();
    }

    public void Stop()
    {
        var current = _current;
        if (current is null || !_isRunning)
        {
            return;
        }

        current.StoppedByUs = true;

        // The connection's own Kill() ends only the shell, leaving whatever it started (a TUI under
        // cmd, the program under `dotnet run`) running.
        try
        {
            using var process = Process.GetProcessById(current.Connection.Pid);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Already exited.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handlerRoutine, [MarshalAs(UnmanagedType.Bool)] bool add);
}
