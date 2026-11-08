using System.Diagnostics;
using System.Runtime.InteropServices;

using Paarde.Core.Helpers;

namespace Paarde.Core.Tests.Helpers;

public class ShellArgumentQuotingTests
{
    // RunConfigurationDetector interpolates a filesystem-derived path (a .csproj's full path) into
    // a command string that's later handed to /bin/sh -c (ProcessCommandRunner/PtyProcessManager) -
    // these round-trip the quoted result through a real shell rather than just asserting on the
    // quoted string's shape, since the only thing that actually matters is what the shell sees.
    [Theory]
    [InlineData("/home/user/My Project/App.csproj")]
    [InlineData("/home/user/\"; touch INJECTED; echo \"/App.csproj")]
    [InlineData("/home/user/$(touch INJECTED)/App.csproj")]
    [InlineData("/home/user/`touch INJECTED`/App.csproj")]
    [InlineData("/home/user/it's a trap/App.csproj")]
    public async Task Quote_RoundTripsThroughAPosixShellAsASingleLiteralArgument(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var tempDirectory = Directory.CreateTempSubdirectory("ShellArgumentQuotingTests_").FullName;
        var command = $"printf '%s' {ShellArgumentQuoting.Quote(path)}";

        try
        {
            var (exitCode, output) = await RunShellCommandAsync(command, tempDirectory);

            Assert.Equal(0, exitCode);
            Assert.Equal(path, output);
            Assert.False(File.Exists(Path.Combine(tempDirectory, "INJECTED")), "The quoted argument executed as shell syntax instead of being treated as a literal string.");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunShellCommandAsync(string command, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            ArgumentList = { "-c", command },
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, output);
    }
}
