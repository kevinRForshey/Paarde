using Paarde.Core.Models;

namespace Paarde.Core.Helpers;

// Maps a tutorial step's resolved HunkLocation (its line range within the file's proposed content,
// from HunkLocationResolver) onto the DiffHunk(s) from DiffBuilder.GroupIntoHunks that actually cover
// those lines. This is what lets "apply this step" eventually mean "apply these specific diff hunks"
// instead of today's per-file apply wearing a per-step label.
public static class ProposedHunkMatcher
{
    public static IReadOnlyList<DiffHunk> FindMatches(IReadOnlyList<DiffHunk> diffHunks, HunkLocation? location)
    {
        if (location == null)
        {
            return Array.Empty<DiffHunk>();
        }

        return diffHunks.Where(hunk => Overlaps(hunk, location)).ToList();
    }

    // A hunk with ProposedLineCount == 0 is a pure deletion - it contributes no lines on the
    // proposed side, so it can never contain a step's snippet (which is always proposed content).
    private static bool Overlaps(DiffHunk hunk, HunkLocation location)
    {
        if (hunk.ProposedLineCount == 0)
        {
            return false;
        }

        var hunkStartLine = hunk.ProposedStartLine - 1;
        var hunkEndLine = hunkStartLine + hunk.ProposedLineCount - 1;

        return hunkStartLine <= location.EndLine && hunkEndLine >= location.StartLine;
    }
}
