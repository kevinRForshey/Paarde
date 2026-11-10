namespace Paarde.Core.Models;

// One row of a side-by-side diff view: the original-side cell and proposed-side cell shown next to
// each other. Either side can be null/empty padding (no DiffLine on that side) - an unpaired
// removal has nothing on the proposed side and vice versa for an unpaired addition - rather than
// leaving a null DiffLineKind? to mean "Unchanged", which would collide with a real Unchanged line.
// OriginalWordSpans/ProposedWordSpans are the word-level diff of this row's own
// OriginalText/ProposedText, set only when both sides are a genuine paired Removed/Added line (not
// padding and not an Unchanged line, which has nothing to highlight within itself) - null otherwise,
// meaning "highlight the whole line the usual way, nothing more precise is available".
public record SideBySideDiffRow(
    string? OriginalText,
    DiffLineKind? OriginalKind,
    string? ProposedText,
    DiffLineKind? ProposedKind,
    IReadOnlyList<DiffLine>? OriginalWordSpans = null,
    IReadOnlyList<DiffLine>? ProposedWordSpans = null)
{
    // Mirrors DiffLine.Prefix, but "" (not "  ") for a blank padding cell (null kind) - there's no
    // line there at all, so even an empty gutter prefix would be misleading.
    public string OriginalPrefix => OriginalKind switch
    {
        DiffLineKind.Removed => "- ",
        DiffLineKind.Unchanged => "  ",
        _ => "",
    };

    public string ProposedPrefix => ProposedKind switch
    {
        DiffLineKind.Added => "+ ",
        DiffLineKind.Unchanged => "  ",
        _ => "",
    };
}
