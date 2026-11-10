using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Core.Services;

// Shells out to run an arbitrary command - the same Process-based pattern
// ClaudeCliRunner already uses for `claude` and GitStatusService uses for `git`, generalized to
// whatever command Settings has configured for build/test. Epic 16's terminal panel and
// Epic 20's Run toolbar both moved onto IPtyProcessManager instead (specs/terminal-emulation T6/
// T7) - this class is IBuildTestRunner's wait-for-exit implementation only, untouched by that
// epic since nothing watches IBuildTestRunner's output live (design.md "Summary").
public class ProcessCommandRunner : IBuildTestRunner
{
    public async Task<CommandResult> RunAsync(string command, string workingDirectoryPath, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Routed through a shell on both platforms (cmd.exe/sh -c) rather than splitting the first
        // token out as the executable - unlike ClaudeCliOptions (one known executable plus fixed
        // arguments), a build/test command is user-typed and commonly relies on shell resolution
        // (PATH, shell builtins, `&&`-chained commands) that invoking the first token directly
        // would not get. On Windows the command line is written verbatim in the `cmd /s /c "<command>"`
        // form (PtyProcessManager's): ArgumentList would escape the command's own quotes as \", which
        // cmd.exe doesn't understand, so any command with a quoted path failed.
        var startInfo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/s /c \"{command}\"" }
            : new ProcessStartInfo { FileName = "/bin/sh", ArgumentList = { "-c", command } };

        startInfo.WorkingDirectory = workingDirectoryPath;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        using var process = new Process { StartInfo = startInfo };

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                standardOutput.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                standardError.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Same as ClaudeCliRunner.RunPromptAsync: WaitForExitAsync throwing doesn't
            // stop the child on its own, so a cancelled/timed-out build would otherwise keep
            // running in the background after this call returns.
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        stopwatch.Stop();

        return new CommandResult(process.ExitCode, standardOutput.ToString().TrimEnd(), standardError.ToString().TrimEnd(), stopwatch.Elapsed);
    }
}
