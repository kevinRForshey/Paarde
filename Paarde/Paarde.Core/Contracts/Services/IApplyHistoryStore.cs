using Paarde.Core.Models;

namespace Paarde.Core.Contracts.Services;

// Session-scoped stack of applied batches, one entry per ApplySelected call that actually wrote
// something - lets "Undo last apply" restore exactly the files that batch touched, even
// when IApplyBackupStore's session-wide baseline for one of those files is now several applies
// in the past. Registered as a singleton (like IPromptResultStore/IApplyBackupStore) so it
// survives page navigation for the lifetime of the running app.
public interface IApplyHistoryStore
{
    // No-ops for an empty batch - ApplySelected calls this once per batch regardless of whether
    // anything was actually applied, so there's nothing worth pushing onto the undo stack.
    void RecordBatch(IReadOnlyList<AppliedFileChange> changes);

    // Pops the most recently recorded batch. False (with an empty list) if nothing's left to undo.
    bool TryPopLastBatch(out IReadOnlyList<AppliedFileChange> changes);

    bool HasHistory { get; }

    // Called after RevertAllApplied - once every tracked file is back at its session
    // baseline, older batches no longer describe a state worth returning to; undoing one would
    // reapply an intermediate edit on top of content that's already been reverted past it.
    void Clear();
}
