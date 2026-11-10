namespace Paarde.Core.Models;

// Result of asking IGitStatusService whether a working directory sits inside a git repository and,
// if so, which files are currently dirty there. DirtyFiles are relative paths in the same
// forward-slash convention as ProposedFileChangeViewModel.FilePath, so callers can intersect the
// two directly without extra normalization.
//
// BranchName is null both for a non-repository and for a
// repository in detached-HEAD state (`git branch --show-current` prints nothing in that case) -
// callers that want to tell those two apart already have IsRepository for that.
public record GitStatus(bool IsRepository, string? BranchName, IReadOnlyList<string> DirtyFiles)
{
    public static readonly GitStatus NotARepository = new(IsRepository: false, BranchName: null, DirtyFiles: Array.Empty<string>());

    public bool IsDirty => DirtyFiles.Count > 0;
}
