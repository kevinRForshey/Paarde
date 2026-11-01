using Paarde.Core.Contracts.Services;

namespace Paarde.Avalonia.Tests.Fakes;

public class FakePtyProcessManager : IPtyProcessManager
{
    public bool IsRunning { get; set; }

    public event Action<byte[]>? OutputReceived;

    public event Action<int?>? Exited;

    public List<(string Command, string WorkingDirectoryPath, int Cols, int Rows)> StartCalls { get; } = [];

    public int StopCallCount { get; private set; }

    public void RaiseOutputReceived(byte[] bytes) => OutputReceived?.Invoke(bytes);

    public void RaiseExited(int? exitCode) => Exited?.Invoke(exitCode);

    public Task StartAsync(string command, string workingDirectoryPath, int cols, int rows, CancellationToken cancellationToken = default)
    {
        StartCalls.Add((command, workingDirectoryPath, cols, rows));
        IsRunning = true;
        return Task.CompletedTask;
    }

    public void Resize(int cols, int rows)
    {
    }

    public void WriteInput(byte[] input)
    {
    }

    public void Stop()
    {
        StopCallCount++;
        IsRunning = false;
    }
}
