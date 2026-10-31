using System.Text;

using Paarde.Core.Services;

namespace Paarde.Core.Tests.Services;

// Spawns real, trivial commands through a real PTY (ConPTY on Windows, forkpty on Linux/macOS) -
// same "test at the process boundary, don't mock it" approach ProcessCommandRunnerTests takes,
// since streaming, exit codes, and Kill() only exist there. Commands differ per platform because
// PtyProcessManager shells through cmd.exe /c on Windows and /bin/sh -c elsewhere.
public class PtyProcessManagerTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly string _workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;
    private readonly PtyProcessManager _manager = new();
    private readonly StringBuilder _output = new();

    public PtyProcessManagerTests()
    {
        _manager.OutputReceived += bytes =>
        {
            lock (_output)
            {
                _output.Append(Encoding.UTF8.GetString(bytes));
            }
        };
    }

    public void Dispose()
    {
        _manager.Stop();
        if (Directory.Exists(_workingDirectory))
        {
            try
            {
                Directory.Delete(_workingDirectory, recursive: true);
            }
            catch (IOException)
            {
                // A just-killed child can still hold the directory as its cwd for a moment on
                // Windows - leaking a temp directory isn't worth failing the test over.
            }
        }
    }

    private static string LongRunningCommand =>
        OperatingSystem.IsWindows() ? "ping -n 30 127.0.0.1 > nul" : "sleep 30";

    private string CurrentOutput()
    {
        lock (_output)
        {
            return _output.ToString();
        }
    }

    private async Task WaitForOutputAsync(string expected)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!CurrentOutput().Contains(expected, StringComparison.Ordinal))
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting for \"{expected}\". Output so far: \"{CurrentOutput()}\"");
            }

            await Task.Delay(25);
        }
    }

    private Task<int?> ExitedTask()
    {
        var exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _manager.Exited += code => exited.TrySetResult(code);
        return exited.Task.WaitAsync(Timeout);
    }

    [Fact]
    public async Task StartAsync_EchoCommand_StreamsOutputBytesAndExitsCleanly()
    {
        var exited = ExitedTask();

        await _manager.StartAsync("echo pty-hello", _workingDirectory, 80, 24);

        await WaitForOutputAsync("pty-hello");
        // The exit code matters as much as the output: a mangled Windows command line once
        // produced "'"echo pty-hello' is not recognized..." - which contains the expected text.
        Assert.Equal(0, await exited);
    }

    [Fact]
    public async Task StartAsync_CommandWithInnerQuotesAndChaining_ReachesTheShellIntact()
    {
        var exited = ExitedTask();

        await _manager.StartAsync("echo \"quoted arg\" && exit 4", _workingDirectory, 80, 24);

        await WaitForOutputAsync("quoted arg");
        Assert.Equal(4, await exited);
    }

    [Fact]
    public async Task StartAsync_CommandExitsOnItsOwn_RaisesExitedWithRealExitCode()
    {
        var exited = ExitedTask();

        await _manager.StartAsync("exit 3", _workingDirectory, 80, 24);

        Assert.Equal(3, await exited);
        Assert.False(_manager.IsRunning);
    }

    [Fact]
    public async Task StartAsync_RunsInTheGivenWorkingDirectory()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "pty-marker.txt"), string.Empty);

        await _manager.StartAsync(OperatingSystem.IsWindows() ? "dir /b" : "ls", _workingDirectory, 80, 24);

        await WaitForOutputAsync("pty-marker.txt");
    }

    [Fact]
    public async Task Stop_WhileRunning_KillsProcessAndRaisesExitedWithNull()
    {
        var exited = ExitedTask();
        await _manager.StartAsync(LongRunningCommand, _workingDirectory, 80, 24);
        Assert.True(_manager.IsRunning);

        _manager.Stop();

        Assert.Null(await exited);
        Assert.False(_manager.IsRunning);
    }

    [Fact]
    public async Task StartAsync_WhileAnotherRunIsActive_StopsThePreviousRunFirst()
    {
        var firstExited = ExitedTask();
        await _manager.StartAsync(LongRunningCommand, _workingDirectory, 80, 24);

        await _manager.StartAsync("echo second-run", _workingDirectory, 80, 24);

        Assert.Null(await firstExited);
        await WaitForOutputAsync("second-run");
    }

    [Fact]
    public async Task Resize_WhileRunning_DoesNotThrow()
    {
        await _manager.StartAsync(LongRunningCommand, _workingDirectory, 80, 24);

        var exception = Record.Exception(() => _manager.Resize(120, 40));

        Assert.Null(exception);
        Assert.True(_manager.IsRunning);
    }

    [Fact]
    public async Task WriteInput_DeliversBytesToTheProcessStdin()
    {
        // Reads one line from stdin and echoes it back with a prefix, so seeing "got:<input>"
        // proves the round trip went through the process itself rather than just the PTY's own
        // local echo of what was typed. cmd's `call` defers %line% expansion until after set /p.
        var command = OperatingSystem.IsWindows()
            ? "set /p line= & call echo got:%line%"
            : "read line; echo \"got:$line\"";
        await _manager.StartAsync(command, _workingDirectory, 80, 24);

        _manager.WriteInput(Encoding.UTF8.GetBytes("round-trip\r"));

        await WaitForOutputAsync("got:round-trip");
    }

    [Fact]
    public void Stop_WhenNothingIsRunning_IsANoOp()
    {
        var exception = Record.Exception(_manager.Stop);

        Assert.Null(exception);
        Assert.False(_manager.IsRunning);
    }

    [Fact]
    public void ResizeAndWriteInput_WhenNothingIsRunning_AreNoOps()
    {
        var exception = Record.Exception(() =>
        {
            _manager.Resize(100, 30);
            _manager.WriteInput([0x61]);
        });

        Assert.Null(exception);
    }
}
