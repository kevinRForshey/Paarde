namespace Paarde.Core.Models;

// Result of an ApplyOrchestrationService.ApplySelected batch. Only files that had
// something selected to apply are represented here - a file nothing was selected on was never
// attempted, so it has no outcome to report. One failed or conflicted file never stops the rest of
// the batch - every selected file gets its own attempt and its own entry here.
public record ApplyBatchResult(IReadOnlyList<FileApplyResult> Results)
{
    public int AppliedCount => Results.Count(r => r.Outcome == FileApplyOutcome.Applied);

    public int ConflictCount => Results.Count(r => r.Outcome == FileApplyOutcome.Conflict);

    public int FailedCount => Results.Count(r => r.Outcome == FileApplyOutcome.Failed);

    // Files skipped because they resolve to a protected location (.git/bin/obj or
    // .gitignore) and the batch wasn't told the user explicitly confirmed writing there.
    public int ProtectedPathBlockedCount => Results.Count(r => r.Outcome == FileApplyOutcome.ProtectedPathBlocked);

    public IEnumerable<FileApplyResult> ConflictedFiles => Results.Where(r => r.Outcome == FileApplyOutcome.Conflict);

    public IEnumerable<FileApplyResult> FailedFiles => Results.Where(r => r.Outcome == FileApplyOutcome.Failed);

    public IEnumerable<FileApplyResult> ProtectedPathBlockedFiles => Results.Where(r => r.Outcome == FileApplyOutcome.ProtectedPathBlocked);

    // Whether the applied files were also staged in git (git add) after writing - false
    // whenever nothing was applied, the working directory isn't a git repository, or git failed to
    // run. Purely informational; a failed/skipped stage never affects AppliedCount or the files on
    // disk, since staging only touches the index.
    public bool FilesStagedInGit { get; init; }
}
