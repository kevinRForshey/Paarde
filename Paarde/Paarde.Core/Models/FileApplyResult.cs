namespace Paarde.Core.Models;

public enum FileApplyOutcome
{
    // The file's selected changes were written to disk.
    Applied,

    // The file changed on disk since this proposal's OriginalContent baseline was captured; the
    // write was skipped rather than silently overwriting whatever the user (or another process) did
    // to the file in the meantime. The caller can offer ProposedFileChangeViewModel.RefreshOriginalContent
    // to re-diff against the current disk content and try again.
    Conflict,

    // The write was attempted and threw (I/O error, permissions, or the path-containment check).
    Failed,

    // The target resolves inside `.git/`, `bin/`, or `obj/`, or is matched by the project's
    // .gitignore, and the caller didn't pass allowProtectedPaths: true to say the user
    // explicitly confirmed writing there anyway. Never attempted - unlike Conflict, there's nothing
    // to re-diff and retry; the caller has to ask again and pass allowProtectedPaths: true.
    ProtectedPathBlocked,
}

// The outcome of attempting to apply one file's selected changes, as part of an
// ApplyOrchestrationService.ApplySelected batch. ErrorMessage is only ever set for Failed.
public record FileApplyResult(string FilePath, FileApplyOutcome Outcome, string? ErrorMessage = null);
