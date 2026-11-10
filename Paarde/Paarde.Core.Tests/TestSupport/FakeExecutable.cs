using System.Diagnostics;

namespace Paarde.Core.Tests.TestSupport;

// A Python script standing in for an external CLI (the `claude` CLI, a language server), so the
// tests that talk to one run on every OS: on Linux/macOS the script itself is the executable
// (shebang + execute bit); on Windows a .cmd wrapper next to it is, which is also how npm-installed
// CLIs look there, so the code under test takes its real Windows launch path.
public sealed class FakeExecutable : IDisposable
{
    private static readonly string PythonCommand = OperatingSystem.IsWindows() ? "python" : "python3";

    // Checked by running it, not by looking it up on PATH: Windows ships a "python" Store stub
    // that is on PATH but only opens the Store.
    private static readonly Lazy<bool> s_isPythonAvailable = new(() =>
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(PythonCommand, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            return process is not null && process.WaitForExit(10_000) && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    });

    private readonly string _scriptPath;
    private readonly string? _wrapperPath;

    // Tests return early when this is false - xUnit 2.x has no runtime skip (see
    // HeadroomServiceIntegrationTests).
    public static bool IsPythonAvailable => s_isPythonAvailable.Value;

    public FakeExecutable(string pythonScript)
    {
        var name = $"fake-{Guid.NewGuid()}";
        _scriptPath = Path.Combine(Path.GetTempPath(), $"{name}.py");

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(_scriptPath, pythonScript);
            _wrapperPath = Path.Combine(Path.GetTempPath(), $"{name}.cmd");
            File.WriteAllText(_wrapperPath, $"@{PythonCommand} \"%~dp0{name}.py\" %*\r\n");
            ExecutablePath = _wrapperPath;
        }
        else
        {
            File.WriteAllText(_scriptPath, $"#!/usr/bin/env python3\n{pythonScript}");
            File.SetUnixFileMode(_scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            ExecutablePath = _scriptPath;
        }
    }

    public string ExecutablePath { get; }

    public void Dispose()
    {
        File.Delete(_scriptPath);
        if (_wrapperPath is not null)
        {
            File.Delete(_wrapperPath);
        }
    }
}
