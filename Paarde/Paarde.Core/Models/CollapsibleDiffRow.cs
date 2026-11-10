namespace Paarde.Core.Models;

// One row of the Diffs page's inline view: either a real DiffLine at diffLines[LineIndex]
// (Line is non-null), or - when CollapsedLineCount > 0 - a placeholder standing in for that many
// hidden Unchanged lines starting at LineIndex, shown as a clickable separator instead of rendering
// them. See DiffBuilder.BuildCollapsibleRows. WordSpans is Line's word-level diff against
// its paired Removed/Added partner, set only when Line actually had one - null for an Unchanged
// line, an unpaired change, or a placeholder.
public record CollapsibleDiffRow(DiffLine? Line, int LineIndex, int CollapsedLineCount, IReadOnlyList<DiffLine>? WordSpans = null) : ICollapsedDiffRegion
{
    public bool IsCollapsedPlaceholder => CollapsedLineCount > 0;

    public string CollapsedSummary => $"Show {CollapsedLineCount} unchanged line{(CollapsedLineCount == 1 ? "" : "s")}";
}
