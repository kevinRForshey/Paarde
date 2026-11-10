using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.ViewModels;

namespace Paarde.Core.Services;

// Writes a proposed file's selected content to disk. The only content we can reliably write back is
// the whole file - there's no way to patch just one line range on a real filesystem write - but what
// that whole-file content *is* now depends on selection: BuildSelectedContent composes it from
// EditedContent (the user's in-editor edits, or the LLM's ProposedContent untouched if the user hasn't
// edited) with any deselected hunks' changes backed out, or OriginalContent untouched if the file's
// whole-file fallback selection is off.
public class FileChangeApplyService : IFileChangeApplyService
{
    public void Apply(ProposedFileChangeViewModel file, string workingDirectoryPath)
    {
        WriteRaw(file.FilePath, file.BuildSelectedContent(), workingDirectoryPath);

        // Record that this write reflects the current selection, so each step's IsApplied
        // is accurate instead of the old MarkAllStepsApplied() workaround that marked every step
        // applied just because one of them triggered the write.
        file.MarkHunksAppliedFromSelection();
    }

    // Extracted out of Apply so undo/revert write-backs - which have raw content to
    // restore rather than a ProposedFileChangeViewModel to compose it from - go through the same
    // path-containment, directory-creation, and encoding/line-ending preservation as a normal
    // apply instead of duplicating it.
    public void WriteRaw(string filePath, string content, string workingDirectoryPath)
    {
        var fullPath = FilePathContainment.Resolve(workingDirectoryPath, filePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Detected from the file this write is about to overwrite (not from the proposal's
        // encoding - there isn't one), so the write round-trips whatever encoding/line endings are
        // already on disk instead of silently normalizing to UTF-8 (no BOM) with LF endings. A
        // brand-new file has nothing to preserve, so this is just today's defaults.
        var encodingInfo = FileEncodingDetector.Detect(fullPath);
        var normalized = NormalizeNewLines(content, encodingInfo.NewLine);

        File.WriteAllText(fullPath, normalized, encodingInfo.Encoding);
    }

    // Rewrites every line ending in `content` to `newLine`. A null `newLine` (FileEncodingDetector
    // found nothing to preserve) leaves content untouched - BuildSelectedContent() already composes
    // hunk-based content with bare '\n' (see DiffBuilder.ComposeContent), so a CRLF file would
    // otherwise come back from Apply with LF endings even though nothing else about it changed.
    private static string NormalizeNewLines(string content, string? newLine)
    {
        if (newLine is null)
        {
            return content;
        }

        // Collapse to LF first so an existing "\r\n" doesn't become "\r" + newLine.
        var normalized = content.Replace("\r\n", "\n").Replace("\r", "\n");
        return newLine == "\n" ? normalized : normalized.Replace("\n", newLine);
    }
}
