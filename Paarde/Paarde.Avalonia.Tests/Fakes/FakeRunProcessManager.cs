using Paarde.Core.Contracts.Services;

namespace Paarde.Avalonia.Tests.Fakes;

public class FakeRunProcessManager : IRunProcessManager
{
    public bool IsRunning { get; set; }

    public event Action<string>? OutputReceived;

    public event Action<int?>? Exited;

    public List<(string Command, string WorkingDirectoryPath)> StartCalls { get; } = [];

    public int StopCallCount { get; private set; }

    public void RaiseOutputReceived(string line) => OutputReceived?.Invoke(line);

    public void RaiseExited(int? exitCode) => Exited?.Invoke(exitCode);

    public void Start(string command, string workingDirectoryPath)
    {
        StartCalls.Add((command, workingDirectoryPath));
        IsRunning = true;
    }

    public void Stop()
    {
        StopCallCount++;
        IsRunning = false;
    }
}
