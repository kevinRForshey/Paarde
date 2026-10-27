using System.Diagnostics;
using System.Runtime.InteropServices;

using Paarde.Core.Contracts.Services;

namespace Paarde.Core.Services;

// specs/build-and-run/design.md: same ProcessStartInfo shelling pattern ProcessCommandRunner
// already uses (cmd.exe/sh -c, so PATH/shell builtins/&&-chains resolve the same way they do
// there), but deliberately not awaiting WaitForExitAsync - the entire point of this class is a
// process that's still running when Start returns, unlike IBuildTestRunner/ITerminalCommandRunner.
public class RunProcessManager : IRunProcessManager
{
    // The "was this stopped by us" flag has to travel with its own process, not live as one
    // shared field on the manager - Process.Exited fires asynchronously, so a second Start()
    // racing in (which resets the flag for the *new* process) could otherwise flip it out from
    // under the old process's own Exited handler before that handler gets a chance to read it.
    private sealed class RunningProcess
    {
        public required Process Process { get; init; }

        public bool StoppedByUs { get; set; }
    }

    private RunningProcess? _current;

    public bool IsRunning => _current is { Process.HasExited: false };

    public event Action<string>? OutputReceived;

    public event Action<int?>? Exited;

    public void Start(string command, string workingDirectoryPath)
    {
        // R4.5: starting a second Run stops whatever's already active first, rather than erroring
        // or leaving two processes running unknowingly.
        Stop();

        var startInfo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ProcessStartInfo { FileName = "cmd.exe", ArgumentList = { "/c", command } }
            : new ProcessStartInfo { FileName = "/bin/sh", ArgumentList = { "-c", command } };

        startInfo.WorkingDirectory = workingDirectoryPath;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var entry = new RunningProcess { Process = process };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                OutputReceived?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                OutputReceived?.Invoke(e.Data);
            }
        };

        process.Exited += (_, _) =>
        {
            // Meaningless when Stop() caused the exit - the caller only ever asked for a kill,
            // not for whatever code killing the process tree happens to produce.
            Exited?.Invoke(entry.StoppedByUs ? null : process.ExitCode);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _current = entry;
    }

    public void Stop()
    {
        var current = _current;
        if (current is null || current.Process.HasExited)
        {
            return;
        }

        current.StoppedByUs = true;

        // R4.4: dotnet run is itself a launcher for the actual app process on some platforms -
        // killing only this one can leave the real one behind.
        current.Process.Kill(entireProcessTree: true);
    }
}
