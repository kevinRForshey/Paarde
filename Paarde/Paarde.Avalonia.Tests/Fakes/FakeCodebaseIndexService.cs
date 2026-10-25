using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Tests.Fakes;

public class FakeCodebaseIndexService : ICodebaseIndexService
{
    public IReadOnlyList<RelevantFileMatch> MatchesToReturn { get; set; } = [];

    public string? LastWorkingDirectoryPath { get; private set; }

    public string? LastQueryText { get; private set; }

    public Task<IReadOnlyList<RelevantFileMatch>> FindRelevantFilesAsync(string workingDirectoryPath, string queryText, int maxResults = 10, CancellationToken cancellationToken = default)
    {
        LastWorkingDirectoryPath = workingDirectoryPath;
        LastQueryText = queryText;
        return Task.FromResult(MatchesToReturn);
    }
}
