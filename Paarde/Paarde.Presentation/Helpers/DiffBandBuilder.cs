using Paarde.Core.Models;

namespace Paarde.Presentation.Helpers;

// A proposed-side line range to paint: an added run (LineCount > 0, 1-based inclusive start) or a
// pure-deletion marker (LineCount == 0 - no proposed-side lines to anchor a band to, so StartLine is
// the insertion point the deleted lines used to sit before).
public readonly record struct DiffBand(int StartLine, int LineCount, bool IsAddition);

// Moved out of Paarde.Avalonia's DiffBackgroundRenderer (specs/winui-frontend F31.7) so both code
// editors paint the same bands from ProposedFileChangeViewModel.DiffLines - a pure line-range
// mapping, kept free of any UI framework so it's unit-testable on its own (T13.3.1).
public static class DiffBandBuilder
{
    // Walks the flat line diff once, tracking the running 1-based line number on the proposed side
    // (the only side the editor actually shows - EditedContent). Consecutive Added lines coalesce
    // into one band; a run of one or more consecutive Removed lines (which consume no proposed-side
    // line number) collapses to a single zero-length marker at the boundary they'd have sat at,
    // rather than one marker per removed line.
    public static IReadOnlyList<DiffBand> Build(IReadOnlyList<DiffLine> diffLines)
    {
        var bands = new List<DiffBand>();
        var proposedLine = 1;
        var addedRunStart = -1;
        var pendingRemovedMarker = false;

        foreach (var line in diffLines)
        {
            if (line.Kind == DiffLineKind.Added)
            {
                if (addedRunStart < 0)
                {
                    addedRunStart = proposedLine;
                }

                proposedLine++;
                pendingRemovedMarker = false;
                continue;
            }

            if (addedRunStart >= 0)
            {
                bands.Add(new DiffBand(addedRunStart, proposedLine - addedRunStart, IsAddition: true));
                addedRunStart = -1;
            }

            if (line.Kind == DiffLineKind.Removed)
            {
                if (!pendingRemovedMarker)
                {
                    bands.Add(new DiffBand(proposedLine, 0, IsAddition: false));
                    pendingRemovedMarker = true;
                }

                continue;
            }

            // Unchanged
            proposedLine++;
            pendingRemovedMarker = false;
        }

        if (addedRunStart >= 0)
        {
            bands.Add(new DiffBand(addedRunStart, proposedLine - addedRunStart, IsAddition: true));
        }

        return bands;
    }
}
