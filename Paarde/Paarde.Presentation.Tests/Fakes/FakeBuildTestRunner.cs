using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeBuildTestRunner : IBuildTestRunner
{
    // One result per RunAsync call (first for the build command, second for the test command),
    // dequeued in call order - mirrors FakeDialogService.ConfirmResultQueue's approach to
    // distinguishing successive calls within a single test.
    public Queue<CommandResult> ResultsToReturn { get; } = new();

    public List<string> CommandsRun { get; } = new();

    public int RunAsyncCallCount { get; private set; }

    // Stands in for IBuildTestRunner.RunAsync's real timeout/cancellation behavior (T12.1.3) -
    // throwing on the next call rather than the ProcessCommandRunnerTests approach of actually
    // waiting out a real cancellation token, since this fake has no process to cancel.
    public bool ThrowOperationCanceledOnNextRun { get; set; }

    public Task<CommandResult> RunAsync(string command, string workingDirectoryPath, CancellationToken cancellationToken = default)
    {
        RunAsyncCallCount++;
        CommandsRun.Add(command);

        if (ThrowOperationCanceledOnNextRun)
        {
            ThrowOperationCanceledOnNextRun = false;
            throw new OperationCanceledException();
        }

        return Task.FromResult(ResultsToReturn.Count > 0 ? ResultsToReturn.Dequeue() : new CommandResult(0, string.Empty, string.Empty, TimeSpan.Zero));
    }
}
