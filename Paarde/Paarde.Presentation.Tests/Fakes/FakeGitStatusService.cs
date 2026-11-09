using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeGitStatusService : IGitStatusService
{
    public GitStatus StatusToReturn { get; set; } = GitStatus.NotARepository;

    public List<string> StageFilesCalls { get; } = [];

    public GitStatus GetStatus(string workingDirectoryPath) => StatusToReturn;

    public bool StageFiles(string workingDirectoryPath, IReadOnlyList<string> relativeFilePaths)
    {
        StageFilesCalls.AddRange(relativeFilePaths);
        return true;
    }
}
