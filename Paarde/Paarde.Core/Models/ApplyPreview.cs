namespace Paarde.Core.Models;

// Summary of what an ApplyOrchestrationService.ApplySelected call would do, shown in a confirmation
// prompt before actually touching disk. ChangeCount counts each selected step/hunk
// individually, and a whole-file fallback (a file with no resolved hunks, applied via
// ProposedFileChangeViewModel.IsSelected directly) counts as a single change.
//
// ProtectedPathCount/ProtectedPathFiles are additional properties rather than primary-
// constructor parameters so every existing `new ApplyPreview(fileCount, changeCount)` call site -
// tests included - keeps compiling and keeps meaning "no protected files" (the default for both),
// without needing to be touched.
// GitDirtyFileCount/GitDirtyFiles are scoped the same way ProtectedPathCount/Files are -
// only the files about to be applied, not every dirty file in the whole repository - so the
// warning is about work this specific apply could clobber, not repo-wide noise.
public record ApplyPreview(int FileCount, int ChangeCount)
{
    public int ProtectedPathCount { get; init; }

    public IReadOnlyList<string> ProtectedPathFiles { get; init; } = Array.Empty<string>();

    public bool IsGitRepository { get; init; }

    public int GitDirtyFileCount { get; init; }

    public IReadOnlyList<string> GitDirtyFiles { get; init; } = Array.Empty<string>();
}
