using System.Diagnostics;
using System.Runtime.InteropServices;

using Paarde.Core.Models;
using Paarde.Core.Services;

namespace Paarde.Core.Tests.Services;

// Covers StdioLanguageServerClient.BuildStartInfo in isolation - the Windows ".cmd shim needs
// cmd.exe /c" decision (Epic 18 follow-up) without spawning a real process, unlike
// StdioLanguageServerClientTests' fake-script-based coverage of the rest of StartAsync.
public class StdioLanguageServerClientBuildStartInfoTests
{
    [Fact]
    public void BuildStartInfo_NativeExecutable_LaunchesItDirectly()
    {
        var executableDirectory = Directory.CreateTempSubdirectory();
        var executablePath = Path.Combine(executableDirectory.FullName, "csharp-ls.exe");
        File.WriteAllText(executablePath, string.Empty);

        try
        {
            var options = new LanguageServerOptions("csharp", executablePath, "--stdio");

            var startInfo = StdioLanguageServerClient.BuildStartInfo(options, executableDirectory.FullName);

            Assert.Equal(executablePath, startInfo.FileName);
            Assert.Equal(["--stdio"], startInfo.ArgumentList);
            Assert.Equal(executableDirectory.FullName, startInfo.WorkingDirectory);
            Assert.False(startInfo.UseShellExecute);
            Assert.True(startInfo.RedirectStandardInput);
            Assert.True(startInfo.RedirectStandardOutput);
        }
        finally
        {
            executableDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void BuildStartInfo_WindowsCmdShim_WrapsWithCmdExe()
    {
        // The .cmd-shim case is Windows-only (ExecutablePathResolver.RequiresShellWrapper is
        // unconditionally false on other platforms) - no xunit.skippablefact dependency pulled in
        // just for this, so this is a no-op (not a failure) off Windows.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var executableDirectory = Directory.CreateTempSubdirectory();
        var executablePath = Path.Combine(executableDirectory.FullName, "typescript-language-server.cmd");
        File.WriteAllText(executablePath, string.Empty);

        try
        {
            var options = new LanguageServerOptions("typescript", executablePath, "--stdio");

            var startInfo = StdioLanguageServerClient.BuildStartInfo(options, executableDirectory.FullName);

            Assert.Equal("cmd.exe", startInfo.FileName);
            Assert.Equal(["/c", executablePath, "--stdio"], startInfo.ArgumentList);
        }
        finally
        {
            executableDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void BuildStartInfo_BareNameResolvedFromPath_UsesTheResolvedFullPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var executableDirectory = Directory.CreateTempSubdirectory();
        var executablePath = Path.Combine(executableDirectory.FullName, "typescript-language-server.cmd");
        File.WriteAllText(executablePath, string.Empty);
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, executableDirectory.FullName, pathVariable));

        try
        {
            // A bare name (as every LanguageServerOptions.Defaults entry uses) rather than an
            // already-resolved absolute path - BuildStartInfo has to search PATH itself rather
            // than trusting options.ExecutablePath as a literal, directly-runnable FileName.
            var options = new LanguageServerOptions("typescript", "typescript-language-server", "--stdio");

            var startInfo = StdioLanguageServerClient.BuildStartInfo(options, executableDirectory.FullName);

            Assert.Equal("cmd.exe", startInfo.FileName);
            Assert.Equal(["/c", executablePath, "--stdio"], startInfo.ArgumentList);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", pathVariable);
            executableDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void BuildStartInfo_UnresolvableBareName_FallsBackToTheLiteralNameUnchanged()
    {
        var options = new LanguageServerOptions("python", "definitely-not-a-real-executable", "--stdio");

        var startInfo = StdioLanguageServerClient.BuildStartInfo(options, Path.GetTempPath());

        Assert.Equal("definitely-not-a-real-executable", startInfo.FileName);
        Assert.Equal(["--stdio"], startInfo.ArgumentList);
    }

    [Fact]
    public void BuildStartInfo_MultiWordArguments_SplitsOnWhitespaceIntoSeparateArguments()
    {
        var executableDirectory = Directory.CreateTempSubdirectory();
        var executablePath = Path.Combine(executableDirectory.FullName, "server.exe");
        File.WriteAllText(executablePath, string.Empty);

        try
        {
            var options = new LanguageServerOptions("python", executablePath, "--stdio --log-level verbose");

            var startInfo = StdioLanguageServerClient.BuildStartInfo(options, executableDirectory.FullName);

            Assert.Equal(["--stdio", "--log-level", "verbose"], startInfo.ArgumentList);
        }
        finally
        {
            executableDirectory.Delete(recursive: true);
        }
    }
}
