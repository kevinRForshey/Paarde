namespace Paarde.Core.Models;

// One step's selection state within a RunSessionFile - the persisted counterpart of
// ProposedHunk. Only what's needed to restore IsSelected is kept; DiffHunks/MatchedDiffHunks are
// derived state that ProposedFileChangeViewModel recomputes from OriginalContent/ProposedContent/
// EditedContent/Hunks on construction, so persisting them would be redundant - and IsApplied
// always resets to false on load anyway (OnOriginalContentChanged), so persisting it would be
// meaningless.
public record RunSessionHunk(string Title, string Explanation, string Snippet, HunkLocation? Location, bool IsSelected);

// One proposed file within a RunSession - the persisted counterpart of ProposedFileChangeViewModel.
// WholeFileSelected is only meaningful when Hunks is empty (the fallback whole-file selection,
// ProposedFileChangeViewModel.IsSelected when HasSelectableHunks is false); it's ignored otherwise.
public record RunSessionFile(
    string FilePath,
    string OriginalContent,
    string ProposedContent,
    string EditedContent,
    bool WholeFileSelected,
    IReadOnlyList<RunSessionHunk> Hunks);

// A durable, on-disk snapshot of one prompt run: the exact prompt sent, the CLI's raw
// output, the parsed tutorial/files (with their current selections), and the apply-history entries
// recorded against this run so far. Written by IRunSessionStore.RecordRun right after a run
// completes and kept current by Resave as the user applies/undoes/reverts changes - see
// IRunSessionStore for exactly when each happens. Restored into the running app on launch by
// IRunSessionStore.RestoreLastSession, and listed/reopened read-only by the History page.
//
// UserPromptText is what the user actually typed into the Prompt page's text box - distinct from
// Prompt, which is the fully composed text sent to the CLI (instruction template + preset/depth
// guidance + attached context files + UserPromptText + working-directory suffix, see
// PromptViewModel.BuildFullPromptAsync). Re-running or forking a past prompt needs the
// former: resending the latter as-is through the normal pipeline would double up the instruction
// template. Defaults to "" so a session file written before this field existed still deserializes
// (Json.NET falls back to a constructor parameter's default when the JSON has no matching
// property) instead of throwing.
public record RunSession(
    string Id,
    DateTimeOffset StartedAt,
    string WorkingDirectoryPath,
    string Prompt,
    string RawOutput,
    string Tutorial,
    IReadOnlyList<RunSessionFile> Files,
    IReadOnlyList<ApplyHistoryEntry> ApplyLog,
    string UserPromptText = "")
{
    // Derived, not persisted-for-a-reason data - mirrors ApplyHistoryEntry.FileCount - for the
    // History page's master list to show without the view having to know Files is a list.
    public int FileCount => Files.Count;
}
