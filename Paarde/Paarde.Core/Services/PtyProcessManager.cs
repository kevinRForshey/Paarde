using System.Runtime.InteropServices;

using Porta.Pty;

using Paarde.Core.Contracts.Services;

namespace Paarde.Core.Services;

// specs/terminal-emulation/design.md: same cmd.exe/sh -c shelling pattern RunProcessManager
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
        var options = new PtyOptions
        {
            Cols = cols,
            Rows = rows,
            Cwd = workingDirectoryPath,
            App = isWindows ? "cmd.exe" : "/bin/sh",
            CommandLine = isWindows ? ["/c", command] : ["-c", command],
        };

        var connection = await PtyProvider.SpawnAsync(options, cancellationToken);
        var entry = new RunningPty { Connection = connection };

        connection.ProcessExited += (_, e) =>
        {
            _isRunning = false;
            // Meaningless when Stop() caused the exit - same reasoning
            // RunProcessManager.Process.Exited already documents.
            Exited?.Invoke(entry.StoppedByUs ? null : e.ExitCode);
        };

        _current = entry;
        _isRunning = true;

        _ = ReadLoopAsync(entry);
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
        current.Connection.Kill();
    }
}
