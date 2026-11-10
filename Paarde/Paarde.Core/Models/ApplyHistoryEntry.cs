namespace Paarde.Core.Models;

public enum ApplyHistoryEntryKind
{
    // An ApplySelected batch wrote at least one file.
    Applied,

    // TryUndoLastApply restored at least one file to its pre-apply content.
    UndoLastApply,

    // RevertAllApplied restored at least one file to its session baseline.
    RevertAllApplied,
}

// One row in the apply-history log shown on the Tutorial page - a lightweight,
// append-only record of what happened and when, independent of IApplyHistoryStore's undo stack
// (which holds full pre-apply file content and gets popped away as it's undone) and
// IApplyBackupStore's session baseline (which holds no timestamps or per-action grouping at all).
// FilePaths only ever lists files an action actually succeeded on - a conflicted, blocked, or
// failed file was never touched, so it has nothing to log here.
public record ApplyHistoryEntry(DateTimeOffset Timestamp, ApplyHistoryEntryKind Kind, IReadOnlyList<string> FilePaths)
{
    public int FileCount => FilePaths.Count;
}
