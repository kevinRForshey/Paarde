namespace Paarde.Core.Models;

// One file's content immediately before a specific ApplySelected batch wrote it - the
// per-batch counterpart to IApplyBackupStore's session-wide baseline, used to undo just the most
// recent apply even when a file has been written more than once in the session.
public record AppliedFileChange(string FilePath, string ContentBeforeApply);
