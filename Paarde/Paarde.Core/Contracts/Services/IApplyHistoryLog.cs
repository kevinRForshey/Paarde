using Paarde.Core.Models;

namespace Paarde.Core.Contracts.Services;

// Session-scoped, append-only log of apply/undo/revert actions, for display on the
// Tutorial page - distinct from IApplyHistoryStore's undo stack, which holds full file content and
// shrinks as it's undone, and from IApplyBackupStore, which has no concept of individual actions
// or timestamps at all. Registered as a singleton so it survives page navigation for the lifetime
// of the running app, like the other apply-related stores.
public interface IApplyHistoryLog
{
    // No-ops for an empty FilePaths list - callers record a batch/undo/revert regardless of
    // whether it actually touched any files, so there's nothing worth logging.
    void Record(ApplyHistoryEntryKind kind, IReadOnlyList<string> filePaths);

    // Oldest first (insertion order) - the caller decides how to order these for display.
    IReadOnlyList<ApplyHistoryEntry> Entries { get; }

    // Replaces the entire log with previously-persisted entries, preserving their original
    // timestamps and order (T7.1.2: restoring the last session on launch) - unlike Record, which
    // always stamps DateTimeOffset.UtcNow and is for new actions, not for replaying old ones.
    void Restore(IReadOnlyList<ApplyHistoryEntry> entries);
}
