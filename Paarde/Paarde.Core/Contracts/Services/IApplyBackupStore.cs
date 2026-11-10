namespace Paarde.Core.Contracts.Services;

// Session-scoped backup of what each file looked like on disk right before an apply overwrote it
// for the first time - the baseline T4.2.2's undo will restore. Registered as a singleton
// (like IPromptResultStore) so it survives page navigation for the lifetime of the running app.
public interface IApplyBackupStore
{
    // No-ops if filePath already has a snapshot - the first write's pre-apply content is the
    // session's undo baseline, not whatever a later apply to the same path left behind.
    void SnapshotIfAbsent(string filePath, string content);

    bool TryGetSnapshot(string filePath, out string content);

    // Every tracked path and its session-baseline content - lets RevertAllApplied know
    // which files to restore without the orchestration service having to separately remember
    // every path it ever snapshotted.
    IReadOnlyDictionary<string, string> AllSnapshots { get; }
}
