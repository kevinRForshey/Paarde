using Paarde.Presentation.Helpers;
using Paarde.Core.Models;

namespace Paarde.Presentation.Tests.Helpers;

public class DiffLineRendererTests
{
    [Fact]
    public void Build_EmptyLine_ReturnsNoSpans()
    {
        var spans = DiffLineRenderer.Build("", null, Array.Empty<SyntaxToken>());

        Assert.Empty(spans);
    }

    [Fact]
    public void Build_NoWordSpansNoSyntaxTokens_ReturnsOneSpanWithNeitherKindNorColor()
    {
        var spans = DiffLineRenderer.Build("line1", null, Array.Empty<SyntaxToken>());

        var span = Assert.Single(spans);
        Assert.Equal("line1", span.Text);
        Assert.Null(span.Kind);
        Assert.Null(span.Color);
    }

    [Fact]
    public void Build_WordSpansOnly_CarriesKindThroughWithNoColor()
    {
        IReadOnlyList<DiffLine> wordSpans =
        [
            new DiffLine(DiffLineKind.Unchanged, "the "),
            new DiffLine(DiffLineKind.Removed, "quick"),
            new DiffLine(DiffLineKind.Unchanged, " fox"),
        ];

        var spans = DiffLineRenderer.Build("the quick fox", wordSpans, Array.Empty<SyntaxToken>());

        Assert.Equal(
            [("the ", (DiffLineKind?)DiffLineKind.Unchanged, (string?)null), ("quick", DiffLineKind.Removed, null), (" fox", DiffLineKind.Unchanged, null)],
            spans.Select(s => (s.Text, s.Kind, s.Color)));
    }

    [Fact]
    public void Build_SyntaxTokensOnly_CarriesColorThroughWithNoKind()
    {
        IReadOnlyList<SyntaxToken> syntaxTokens =
        [
            new SyntaxToken("public", "#569CD6"),
            new SyntaxToken(" x", null),
        ];

        var spans = DiffLineRenderer.Build("public x", null, syntaxTokens);

        Assert.Equal(
            [("public", (DiffLineKind?)null, "#569CD6"), (" x", null, (string?)null)],
            spans.Select(s => (s.Text, s.Kind, s.Color)));
    }

    [Fact]
    public void Build_WordSpanAndSyntaxTokenBoundariesMisaligned_SplitsAtEveryBoundary()
    {
        // "ab" is one word-diff span (Removed) but two syntax tokens ("a" then "b") - the merged
        // result must split at the syntax boundary too, so "a" and "b" keep their own colors while
        // both stay marked Removed.
        IReadOnlyList<DiffLine> wordSpans = [new DiffLine(DiffLineKind.Removed, "ab")];
        IReadOnlyList<SyntaxToken> syntaxTokens = [new SyntaxToken("a", "#111111"), new SyntaxToken("b", "#222222")];

        var spans = DiffLineRenderer.Build("ab", wordSpans, syntaxTokens);

        Assert.Equal(
            [("a", DiffLineKind.Removed, "#111111"), ("b", DiffLineKind.Removed, "#222222")],
            spans.Select(s => (s.Text, s.Kind, s.Color)));
    }

    [Fact]
    public void Build_SpansReconstructTheOriginalLineExactly()
    {
        IReadOnlyList<DiffLine> wordSpans = [new DiffLine(DiffLineKind.Unchanged, "foo"), new DiffLine(DiffLineKind.Added, "-bar")];
        IReadOnlyList<SyntaxToken> syntaxTokens = [new SyntaxToken("foo-", "#111111"), new SyntaxToken("bar", null)];

        var spans = DiffLineRenderer.Build("foo-bar", wordSpans, syntaxTokens);

        Assert.Equal("foo-bar", string.Concat(spans.Select(s => s.Text)));
    }
}
