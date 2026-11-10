using Paarde.Core.Models;

namespace Paarde.Presentation.Helpers;

// Merges two independent tokenizations of the same line - DiffBuilder's word-level
// diff (which words changed against this line's Removed/Added partner, as a background highlight)
// and SyntaxTokenizer's TextMate tokens (what color each token's syntax gets, as a foreground) -
// into one ordered list of runs, each with a single unambiguous Kind/Color pair, since neither
// tokenization alone should suppress the other. Pulled out of DiffWordSpanTextBlock so the merge
// itself can be unit tested without needing a live Avalonia visual tree.
public static class DiffLineRenderer
{
    public readonly record struct RenderSpan(string Text, DiffLineKind? Kind, string? Color);

    public static IReadOnlyList<RenderSpan> Build(string lineText, IReadOnlyList<DiffLine>? wordSpans, IReadOnlyList<SyntaxToken> syntaxTokens)
    {
        if (string.IsNullOrEmpty(lineText))
        {
            return Array.Empty<RenderSpan>();
        }

        var diffSpans = NormalizeDiffSpans(wordSpans, lineText.Length);
        var syntaxSpans = NormalizeSyntaxSpans(syntaxTokens, lineText.Length);

        return MergeSpans(diffSpans, syntaxSpans, lineText);
    }

    private readonly record struct LengthSpan<T>(int Length, T Value);

    // WordSpans is null for an Unchanged line, a side-by-side padding cell, or a change that never
    // paired with a partner to diff against - all of which mean "no word-level highlight anywhere
    // in this line", represented as a single span covering it with no Kind.
    private static List<LengthSpan<DiffLineKind?>> NormalizeDiffSpans(IReadOnlyList<DiffLine>? wordSpans, int totalLength) =>
        wordSpans is null
            ? [new LengthSpan<DiffLineKind?>(totalLength, null)]
            : wordSpans.Select(span => new LengthSpan<DiffLineKind?>(span.Text.Length, span.Kind)).ToList();

    // An empty token list means no grammar was found for this file (or FilePath is null/empty) -
    // "no syntax coloring anywhere in this line", represented the same way as above.
    private static List<LengthSpan<string?>> NormalizeSyntaxSpans(IReadOnlyList<SyntaxToken> tokens, int totalLength) =>
        tokens.Count == 0
            ? [new LengthSpan<string?>(totalLength, null)]
            : tokens.Select(token => new LengthSpan<string?>(token.Text.Length, token.Color)).ToList();

    // Both diffSpans and syntaxSpans partition the same lineText end-to-end (each sums its Lengths
    // to lineText.Length), just along different boundaries - walks both in lockstep, splitting at
    // whichever boundary comes first, so every resulting run has a single, unambiguous Kind/Color.
    private static List<RenderSpan> MergeSpans(List<LengthSpan<DiffLineKind?>> diffSpans, List<LengthSpan<string?>> syntaxSpans, string lineText)
    {
        var result = new List<RenderSpan>();
        var position = 0;
        var di = 0;
        var si = 0;
        var diffRemaining = diffSpans[0].Length;
        var syntaxRemaining = syntaxSpans[0].Length;

        while (di < diffSpans.Count && si < syntaxSpans.Count)
        {
            var take = Math.Min(diffRemaining, syntaxRemaining);

            if (take > 0)
            {
                result.Add(new RenderSpan(lineText.Substring(position, take), diffSpans[di].Value, syntaxSpans[si].Value));
                position += take;
            }

            diffRemaining -= take;
            syntaxRemaining -= take;

            if (diffRemaining == 0 && ++di < diffSpans.Count)
            {
                diffRemaining = diffSpans[di].Length;
            }

            if (syntaxRemaining == 0 && ++si < syntaxSpans.Count)
            {
                syntaxRemaining = syntaxSpans[si].Length;
            }
        }

        return result;
    }
}
