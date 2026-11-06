using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Core.Tests.Services.Fakes;

// Mirrors Paarde.Avalonia.Tests/Fakes/FakeBuildTestRunner.cs - duplicated rather than shared
// since Paarde.Core.Tests doesn't reference the Avalonia test project. Lets HeadroomServiceTests
// assert the exact command/working directory IHeadroomService passes through, without spawning a
// real `headroom`/`pip` process.
public class FakeBuildTestRunner : IBuildTestRunner
{
    public Queue<CommandResult> ResultsToReturn { get; } = new();

    public List<(string Command, string WorkingDirectoryPath)> Calls { get; } = [];

    public Task<CommandResult> RunAsync(string command, string workingDirectoryPath, CancellationToken cancellationToken = default)
    {
        Calls.Add((command, workingDirectoryPath));

        return Task.FromResult(ResultsToReturn.Count > 0 ? ResultsToReturn.Dequeue() : new CommandResult(0, string.Empty, string.Empty, TimeSpan.Zero));
    }
}
