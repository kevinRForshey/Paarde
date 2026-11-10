namespace Paarde.Core.Models;

// Shared shape of a collapsed-region placeholder row, implemented by both
// CollapsibleDiffRow (inline view) and CollapsibleSideBySideDiffRow (side-by-side view) so
// DiffsDetailControl's single "expand this region" click handler works against either without
// caring which view mode produced the row that was clicked.
public interface ICollapsedDiffRegion
{
    // The segment's own start index within the DiffLines it was built from - also this
    // placeholder's identity, since DiffsDetailControl tracks which placeholders have been expanded
    // by this value.
    int LineIndex { get; }

    int CollapsedLineCount { get; }

    bool IsCollapsedPlaceholder { get; }
}
