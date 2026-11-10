namespace Paarde.Core.Models;

// The side-by-side view's equivalent of CollapsibleDiffRow: either a real paired row (Row
// is non-null), or a collapsed-region placeholder spanning both columns. LineIndex is only
// meaningful on a placeholder - a real row's position here isn't 1:1 with a single DiffLines index
// once BuildSideBySideRows has padded a run, and nothing needs it there. See
// DiffBuilder.BuildCollapsibleSideBySideRows.
public record CollapsibleSideBySideDiffRow(SideBySideDiffRow? Row, int LineIndex, int CollapsedLineCount) : ICollapsedDiffRegion
{
    public bool IsCollapsedPlaceholder => CollapsedLineCount > 0;

    public string CollapsedSummary => $"Show {CollapsedLineCount} unchanged line{(CollapsedLineCount == 1 ? "" : "s")}";
}
