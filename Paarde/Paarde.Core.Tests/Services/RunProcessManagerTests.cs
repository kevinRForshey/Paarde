using System.Runtime.Versioning;

using Paarde.Core.Services;

namespace Paarde.Core.Tests.Services;

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]

// Same real-shell-command approach ProcessCommandRunnerTests uses rather than mocking Process -
// the behavior under test (a process still running when Start returns, live output streaming,
// Stop killing it, double-start stopping the prior run) only exists at that boundary.
public class RunProcessManagerTests : IDisposable
{
    private readonly string _workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;
    private readonly RunProcessManager _manager = new();

    public void Dispose()
    {
        _manager.Stop();

        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    [Fact]
    public void Start_LongLivedCommand_ReturnsWhileStillRunning()
    {
        _manager.Start("sleep 5", _workingDirectory);

        Assert.True(_manager.IsRunning);
    }

    [Fact]
    public async Task Start_StreamsOutputAsItArrives()
    {
        var receivedLines = new List<string>();
        var tcs = new TaskCompletionSource();
        _manager.OutputReceived += line =>
        {
            receivedLines.Add(line);
            tcs.TrySetResult();
        };

        _manager.Start("printf 'hello\\n'", _workingDirectory);

        await WaitWithTimeoutAsync(tcs.Task);

        Assert.Contains("hello", receivedLines);
    }

    [Fact]
    public async Task Stop_KillsTheRunningProcessAndRaisesExitedWithNullExitCode()
    {
        var exitedTcs = new TaskCompletionSource<int?>();
        _manager.Exited += exitCode => exitedTcs.TrySetResult(exitCode);

        _manager.Start("sleep 30", _workingDirectory);
        _manager.Stop();

        var exitCode = await WaitWithTimeoutAsync(exitedTcs.Task);

        Assert.Null(exitCode);
        Assert.False(_manager.IsRunning);
    }

    [Fact]
    public void Stop_NothingRunning_IsANoOp()
    {
        _manager.Stop();

        Assert.False(_manager.IsRunning);
    }

    [Fact]
    public async Task Start_WhileAlreadyRunning_StopsThePreviousProcessFirst()
    {
        var exitedCodes = new List<int?>();
        _manager.Exited += exitedCodes.Add;

        _manager.Start("sleep 30", _workingDirectory);
        _manager.Start("sleep 30", _workingDirectory);

        await WaitForConditionAsync(() => exitedCodes.Count >= 1);

        Assert.Equal([null], exitedCodes);
        Assert.True(_manager.IsRunning);
    }

    [Fact]
    public async Task Exited_RaisedWithRealExitCodeWhenProcessExitsOnItsOwn()
    {
        var exitedTcs = new TaskCompletionSource<int?>();
        _manager.Exited += exitCode => exitedTcs.TrySetResult(exitCode);

        _manager.Start("exit 7", _workingDirectory);

        var exitCode = await WaitWithTimeoutAsync(exitedTcs.Task);

        Assert.Equal(7, exitCode);
    }

    private static async Task<T> WaitWithTimeoutAsync<T>(Task<T> task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(task, completed);
        return await task;
    }

    private static async Task WaitWithTimeoutAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(task, completed);
        await task;
    }

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.True(condition());
    }
}
