using Paarde.Core.Models;

namespace Paarde.Presentation.Helpers;

// Pure function (no Avalonia dependency) so BreadcrumbPathBuilderTests
// can exercise it directly - BreadcrumbBar.axaml only ever renders whatever this returns.
public static class BreadcrumbPathBuilder
{
    // root is the synthetic file root GetDocumentSymbolsAsync returns (design.md #3) - its own
    // Name/Range never appear in the result, only its Children (the file's actual top-level
    // symbols) and their descendants.
    public static IReadOnlyList<LspDocumentSymbol> Build(LspDocumentSymbol? root, LspPosition? caretPosition)
    {
        if (root is null || caretPosition is null)
        {
            return [];
        }

        var chain = new List<LspDocumentSymbol>();
        var candidates = root.Children;

        while (candidates.FirstOrDefault(symbol => Contains(symbol.Range, caretPosition)) is { } match)
        {
            chain.Add(match);
            candidates = match.Children;
        }

        return chain;
    }

    // Inclusive of both endpoints, matching LSP's own Range semantics - a caret sitting exactly on
    // a symbol's opening or closing line is still "inside" it as long as it isn't before Start or
    // after End on that line.
    private static bool Contains(LspRange range, LspPosition position) =>
        ComparePositions(position, range.Start) >= 0 && ComparePositions(position, range.End) <= 0;

    private static int ComparePositions(LspPosition a, LspPosition b) =>
        a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Character.CompareTo(b.Character);
}
