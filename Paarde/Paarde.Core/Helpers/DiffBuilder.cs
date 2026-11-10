using System.Text.RegularExpressions;

using Paarde.Core.Models;

namespace Paarde.Core.Helpers;

// Builds a line-level diff (which lines were added/removed/kept) between an original and proposed
// file's contents, using a standard LCS dynamic-programming table.
public static class DiffBuilder
{
    // Above this many original*proposed line (or, for BuildWordDiff, token) pairs the O(n*m) LCS
    // table gets too large; fall back to treating the whole thing as replaced rather than risk
    // excessive memory use on huge files/lines.
    private const int MaxLcsCells = 4_000_000;

    // Splits a line into word/non-word/whitespace runs without losing any characters -
    // joining every token back together reproduces the original line exactly - so BuildWordDiff can
    // diff at word granularity while still being able to reconstruct the full line from its tokens.
    private static readonly Regex WordBoundaryRegex = new(@"(?<=\w)(?=\W)|(?<=\W)(?=\w)", RegexOptions.Compiled);

    public static IReadOnlyList<DiffLine> BuildLineDiff(string originalText, string proposedText) =>
        ComputeDiff(SplitLines(originalText), SplitLines(proposedText));

    // The same LCS diff as BuildLineDiff, just tokenized at the word level instead of split
    // into lines - used to find which part of a single changed line actually differs, for intra-line
    // highlighting. Only meaningful for an actual Removed/Added pair (see
    // BuildSideBySideRows/ComputeInlineWordSpans, which are the only callers).
    public static IReadOnlyList<DiffLine> BuildWordDiff(string originalLine, string proposedLine) =>
        ComputeDiff(Tokenize(originalLine), Tokenize(proposedLine));

    private static string[] Tokenize(string text) =>
        string.IsNullOrEmpty(text) ? Array.Empty<string>() : WordBoundaryRegex.Split(text);

    // Returns every token in wordDiff except those with the given Kind - used to reconstruct one
    // side of a paired Removed/Added line from its word-level diff: excluding Added tokens
    // reconstructs the original line (with its changed words marked Removed), excluding Removed
    // tokens reconstructs the proposed line (with its changed words marked Added).
    private static IReadOnlyList<DiffLine> ExtractSide(IReadOnlyList<DiffLine> wordDiff, DiffLineKind excludedKind) =>
        wordDiff.Where(token => token.Kind != excludedKind).ToList();

    private static IReadOnlyList<DiffLine> ComputeDiff(string[] original, string[] proposed)
    {
        if ((long)original.Length * proposed.Length > MaxLcsCells)
        {
            return original.Select(token => new DiffLine(DiffLineKind.Removed, token))
                .Concat(proposed.Select(token => new DiffLine(DiffLineKind.Added, token)))
                .ToList();
        }

        var lcsLengths = ComputeLcsLengths(original, proposed);

        var result = new List<DiffLine>();
        Backtrack(lcsLengths, original, proposed, result);
        return result;
    }

    // Pairs up a flat line-level diff into side-by-side rows for the diff viewer's
    // side-by-side mode. Each maximal run of consecutive Removed/Added lines is split back into
    // its removed-only and added-only sub-sequences (preserving each's own relative order) and
    // paired off index-by-index, so a changed line's old and new text land on the same row -
    // padding the shorter side with a blank cell (null text/kind) when the run has more lines on
    // one side than the other. An Unchanged line always gets its own row with identical text on
    // both sides.
    public static IReadOnlyList<SideBySideDiffRow> BuildSideBySideRows(IReadOnlyList<DiffLine> diffLines)
    {
        var rows = new List<SideBySideDiffRow>();
        var removedRun = new List<DiffLine>();
        var addedRun = new List<DiffLine>();

        void FlushRun()
        {
            var pairCount = Math.Max(removedRun.Count, addedRun.Count);

            for (var i = 0; i < pairCount; i++)
            {
                var left = i < removedRun.Count ? removedRun[i] : null;
                var right = i < addedRun.Count ? addedRun[i] : null;

                // Only a genuine pair (not one side padded out) has anything on the other
                // side to diff against at word level.
                IReadOnlyList<DiffLine>? originalWordSpans = null;
                IReadOnlyList<DiffLine>? proposedWordSpans = null;

                if (left is not null && right is not null)
                {
                    var wordDiff = BuildWordDiff(left.Text, right.Text);
                    originalWordSpans = ExtractSide(wordDiff, excludedKind: DiffLineKind.Added);
                    proposedWordSpans = ExtractSide(wordDiff, excludedKind: DiffLineKind.Removed);
                }

                rows.Add(new SideBySideDiffRow(left?.Text, left?.Kind, right?.Text, right?.Kind, originalWordSpans, proposedWordSpans));
            }

            removedRun.Clear();
            addedRun.Clear();
        }

        foreach (var line in diffLines)
        {
            switch (line.Kind)
            {
                case DiffLineKind.Removed:
                    removedRun.Add(line);
                    break;
                case DiffLineKind.Added:
                    addedRun.Add(line);
                    break;
                default:
                    FlushRun();
                    rows.Add(new SideBySideDiffRow(line.Text, line.Kind, line.Text, line.Kind));
                    break;
            }
        }

        FlushRun();

        return rows;
    }

    // Splits a flat line-level diff into display rows for the Diffs page's inline view, where
    // a long run of Unchanged lines far from any change is folded into a single clickable
    // placeholder instead of rendering every line - expandedSegmentStarts (keyed by the placeholder's
    // own LineIndex, i.e. the segment's start index within diffLines) names the ones the user has
    // already expanded back to their real lines. Uses the same default contextLines as
    // GroupIntoHunks so a DiffHunk's own leading/trailing context is always part of a kept-visible
    // segment here too, never folded away - callers that scroll to a DiffHunk's SourceRangeStart can
    // rely on that row always being present.
    public static IReadOnlyList<CollapsibleDiffRow> BuildCollapsibleRows(
        IReadOnlyList<DiffLine> diffLines,
        IReadOnlySet<int> expandedSegmentStarts,
        int contextLines = 3)
    {
        var rows = new List<CollapsibleDiffRow>();
        var wordSpansByIndex = ComputeInlineWordSpans(diffLines);

        foreach (var segment in FindCollapsibleSegments(diffLines, contextLines))
        {
            if (segment.IsCollapsible && !expandedSegmentStarts.Contains(segment.Start))
            {
                rows.Add(new CollapsibleDiffRow(null, segment.Start, segment.End - segment.Start));
                continue;
            }

            for (var i = segment.Start; i < segment.End; i++)
            {
                rows.Add(new CollapsibleDiffRow(diffLines[i], i, 0, wordSpansByIndex.GetValueOrDefault(i)));
            }
        }

        return rows;
    }

    // The inline view's equivalent of what BuildSideBySideRows's FlushRun computes inline -
    // pairs up each run's Removed/Added lines index-by-index and, for each actual pair, works out
    // that pair's word-level diff - but keyed by the line's own index within diffLines (rather than
    // built into rows), since the inline view keeps one row per diffLines index regardless of
    // pairing. An unpaired line past the shorter side's count has nothing to diff against, so it's
    // left out of the map entirely (falls back to a plain, whole-line highlight).
    private static Dictionary<int, IReadOnlyList<DiffLine>> ComputeInlineWordSpans(IReadOnlyList<DiffLine> diffLines)
    {
        var spansByIndex = new Dictionary<int, IReadOnlyList<DiffLine>>();
        var removedRun = new List<(int Index, DiffLine Line)>();
        var addedRun = new List<(int Index, DiffLine Line)>();

        void FlushRun()
        {
            var pairCount = Math.Min(removedRun.Count, addedRun.Count);

            for (var i = 0; i < pairCount; i++)
            {
                var wordDiff = BuildWordDiff(removedRun[i].Line.Text, addedRun[i].Line.Text);
                spansByIndex[removedRun[i].Index] = ExtractSide(wordDiff, excludedKind: DiffLineKind.Added);
                spansByIndex[addedRun[i].Index] = ExtractSide(wordDiff, excludedKind: DiffLineKind.Removed);
            }

            removedRun.Clear();
            addedRun.Clear();
        }

        for (var i = 0; i < diffLines.Count; i++)
        {
            switch (diffLines[i].Kind)
            {
                case DiffLineKind.Removed:
                    removedRun.Add((i, diffLines[i]));
                    break;
                case DiffLineKind.Added:
                    addedRun.Add((i, diffLines[i]));
                    break;
                default:
                    FlushRun();
                    break;
            }
        }

        FlushRun();

        return spansByIndex;
    }

    // The side-by-side equivalent of BuildCollapsibleRows - same segmenting, but each
    // visible segment's lines are paired up via the existing BuildSideBySideRows rather than listed
    // one row per line. A segment boundary never splits a Removed/Added run (FindCollapsibleSegments
    // only ever cuts through Unchanged lines), so pairing each visible segment independently gives
    // the same result as pairing the whole file would, just with collapsed segments cut out. A
    // paired row's LineIndex is left at -1 - once BuildSideBySideRows has padded a run, a row's
    // position here no longer corresponds 1:1 with a single diffLines index, and nothing needs it
    // (unlike a placeholder's LineIndex, which is this row type's expand-click identity too).
    public static IReadOnlyList<CollapsibleSideBySideDiffRow> BuildCollapsibleSideBySideRows(
        IReadOnlyList<DiffLine> diffLines,
        IReadOnlySet<int> expandedSegmentStarts,
        int contextLines = 3)
    {
        var rows = new List<CollapsibleSideBySideDiffRow>();

        foreach (var segment in FindCollapsibleSegments(diffLines, contextLines))
        {
            if (segment.IsCollapsible && !expandedSegmentStarts.Contains(segment.Start))
            {
                rows.Add(new CollapsibleSideBySideDiffRow(null, segment.Start, segment.End - segment.Start));
                continue;
            }

            var segmentLines = diffLines.Skip(segment.Start).Take(segment.End - segment.Start).ToList();

            foreach (var row in BuildSideBySideRows(segmentLines))
            {
                rows.Add(new CollapsibleSideBySideDiffRow(row, -1, 0));
            }
        }

        return rows;
    }

    // One contiguous [Start, End) range of diffLines that either must stay visible (a changed
    // region, or unchanged context close enough to one to matter) or is eligible to collapse (an
    // unchanged run far enough from any change that hiding it loses nothing a reader would miss).
    private readonly record struct DiffSegment(int Start, int End, bool IsCollapsible);

    private static List<DiffSegment> FindCollapsibleSegments(IReadOnlyList<DiffLine> diffLines, int contextLines)
    {
        var segments = new List<DiffSegment>();

        if (diffLines.Count == 0)
        {
            return segments;
        }

        var changeRanges = FindChangeRanges(diffLines);

        if (changeRanges.Count == 0)
        {
            // No change anywhere in the file, so there's no adjacent change needing context on
            // either side - the whole thing is collapsible (unless it's too short to bother, which
            // AddUnchangedRun's own length check below handles).
            AddUnchangedRun(segments, 0, diffLines.Count, leftNeed: 0, rightNeed: 0);
            return segments;
        }

        var cursor = 0;

        for (var i = 0; i < changeRanges.Count; i++)
        {
            var (changeStart, changeEnd) = changeRanges[i];

            if (changeStart > cursor)
            {
                // The run before the first change has no earlier change to keep context for; every
                // later run sits between two changes and needs contextLines of context on both sides.
                var leftNeed = i == 0 ? 0 : contextLines;
                AddUnchangedRun(segments, cursor, changeStart, leftNeed, contextLines);
            }

            segments.Add(new DiffSegment(changeStart, changeEnd, IsCollapsible: false));
            cursor = changeEnd;
        }

        if (cursor < diffLines.Count)
        {
            // The run after the last change has no later change to keep context for.
            AddUnchangedRun(segments, cursor, diffLines.Count, contextLines, rightNeed: 0);
        }

        return segments;
    }

    // Splits one run of Unchanged lines into up to three segments: up to leftNeed lines of visible
    // context, a collapsible middle, and up to rightNeed lines of visible context - or, if the run
    // isn't even long enough to need collapsing, a single visible segment covering all of it.
    private static void AddUnchangedRun(List<DiffSegment> segments, int start, int end, int leftNeed, int rightNeed)
    {
        if (end - start <= leftNeed + rightNeed)
        {
            segments.Add(new DiffSegment(start, end, IsCollapsible: false));
            return;
        }

        if (leftNeed > 0)
        {
            segments.Add(new DiffSegment(start, start + leftNeed, IsCollapsible: false));
        }

        segments.Add(new DiffSegment(start + leftNeed, end - rightNeed, IsCollapsible: true));

        if (rightNeed > 0)
        {
            segments.Add(new DiffSegment(end - rightNeed, end, IsCollapsible: false));
        }
    }

    // Groups a flat line-level diff into unified-diff-style hunks: each changed region plus up to
    // contextLines of surrounding unchanged lines. Changed regions separated by no more than
    // 2 * contextLines of unchanged lines are merged into one hunk (their shared context would
    // otherwise overlap); regions farther apart than that become separate hunks, and runs of
    // unchanged lines outside any hunk's context are dropped rather than carried through.
    public static IReadOnlyList<DiffHunk> GroupIntoHunks(IReadOnlyList<DiffLine> diffLines, int contextLines = 3)
    {
        var changeRanges = FindChangeRanges(diffLines);

        if (changeRanges.Count == 0)
        {
            return Array.Empty<DiffHunk>();
        }

        var mergedRanges = MergeNearbyRanges(changeRanges, contextLines);
        var originalPrefixCounts = ComputePrefixCounts(diffLines, excludedKind: DiffLineKind.Added);
        var proposedPrefixCounts = ComputePrefixCounts(diffLines, excludedKind: DiffLineKind.Removed);

        var hunks = new List<DiffHunk>(mergedRanges.Count);

        foreach (var (changeStart, changeEnd) in mergedRanges)
        {
            var start = Math.Max(0, changeStart - contextLines);
            var end = Math.Min(diffLines.Count, changeEnd + contextLines);
            hunks.Add(BuildHunk(diffLines, start, end, originalPrefixCounts, proposedPrefixCounts));
        }

        return hunks;
    }

    // Finds the maximal runs of consecutive non-Unchanged lines, as [start, end) index pairs.
    private static List<(int Start, int End)> FindChangeRanges(IReadOnlyList<DiffLine> diffLines)
    {
        var ranges = new List<(int Start, int End)>();
        var i = 0;

        while (i < diffLines.Count)
        {
            if (diffLines[i].Kind == DiffLineKind.Unchanged)
            {
                i++;
                continue;
            }

            var start = i;

            while (i < diffLines.Count && diffLines[i].Kind != DiffLineKind.Unchanged)
            {
                i++;
            }

            ranges.Add((start, i));
        }

        return ranges;
    }

    // Merges adjacent change ranges whose unchanged gap is small enough that their context windows
    // would overlap.
    private static List<(int Start, int End)> MergeNearbyRanges(List<(int Start, int End)> ranges, int contextLines)
    {
        var merged = new List<(int Start, int End)> { ranges[0] };

        for (var i = 1; i < ranges.Count; i++)
        {
            var last = merged[^1];
            var gap = ranges[i].Start - last.End;

            if (gap <= contextLines * 2)
            {
                merged[^1] = (last.Start, ranges[i].End);
            }
            else
            {
                merged.Add(ranges[i]);
            }
        }

        return merged;
    }

    // counts[i] = number of lines among diffLines[0..i) that consume a line of the side being
    // counted (i.e. everything except the kind that side doesn't contribute to).
    private static int[] ComputePrefixCounts(IReadOnlyList<DiffLine> diffLines, DiffLineKind excludedKind)
    {
        var counts = new int[diffLines.Count + 1];

        for (var i = 0; i < diffLines.Count; i++)
        {
            counts[i + 1] = counts[i] + (diffLines[i].Kind != excludedKind ? 1 : 0);
        }

        return counts;
    }

    private static DiffHunk BuildHunk(
        IReadOnlyList<DiffLine> diffLines,
        int start,
        int end,
        int[] originalPrefixCounts,
        int[] proposedPrefixCounts)
    {
        var lines = diffLines.Skip(start).Take(end - start).ToList();
        var originalLineCount = originalPrefixCounts[end] - originalPrefixCounts[start];
        var proposedLineCount = proposedPrefixCounts[end] - proposedPrefixCounts[start];

        // A zero count means this side contributes nothing (a pure insertion or pure deletion); the
        // unified diff convention is then to report the insertion point rather than a 1-based line.
        var originalStartLine = originalLineCount > 0 ? originalPrefixCounts[start] + 1 : originalPrefixCounts[start];
        var proposedStartLine = proposedLineCount > 0 ? proposedPrefixCounts[start] + 1 : proposedPrefixCounts[start];

        return new DiffHunk(originalStartLine, originalLineCount, proposedStartLine, proposedLineCount, lines, start, end);
    }

    // Recomposes file content from diffLines, taking each hunk's proposed (Added) lines where
    // isSelected approves it and its original (Removed) lines otherwise, so an unselected hunk's
    // change is left out entirely rather than partially applied. Lines outside every hunk's
    // SourceRangeStart/End are always Unchanged (hunks are built to cover every changed region) and
    // pass through regardless of selection.
    public static string ComposeContent(IReadOnlyList<DiffLine> diffLines, IReadOnlyList<DiffHunk> hunks, Func<DiffHunk, bool> isSelected)
    {
        var result = new List<string>(diffLines.Count);
        var hunkIndex = 0;

        for (var i = 0; i < diffLines.Count; i++)
        {
            while (hunkIndex < hunks.Count && i >= hunks[hunkIndex].SourceRangeEnd)
            {
                hunkIndex++;
            }

            var line = diffLines[i];
            var inCurrentHunk = hunkIndex < hunks.Count && i >= hunks[hunkIndex].SourceRangeStart;

            if (!inCurrentHunk)
            {
                // Outside any hunk's range - guaranteed Unchanged, since GroupIntoHunks' change
                // ranges (plus context) are exactly what became hunks.
                result.Add(line.Text);
                continue;
            }

            var keepAdded = isSelected(hunks[hunkIndex]);

            if (line.Kind == DiffLineKind.Unchanged || (line.Kind == DiffLineKind.Added && keepAdded) || (line.Kind == DiffLineKind.Removed && !keepAdded))
            {
                result.Add(line.Text);
            }
        }

        return string.Join('\n', result);
    }

    private static string[] SplitLines(string text) =>
        string.IsNullOrEmpty(text) ? Array.Empty<string>() : text.Replace("\r\n", "\n").Split('\n');

    private static int[,] ComputeLcsLengths(string[] original, string[] proposed)
    {
        var lengths = new int[original.Length + 1, proposed.Length + 1];

        for (var i = original.Length - 1; i >= 0; i--)
        {
            for (var j = proposed.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = original[i] == proposed[j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        return lengths;
    }

    private static void Backtrack(int[,] lengths, string[] original, string[] proposed, List<DiffLine> result)
    {
        var i = 0;
        var j = 0;

        while (i < original.Length && j < proposed.Length)
        {
            if (original[i] == proposed[j])
            {
                result.Add(new DiffLine(DiffLineKind.Unchanged, original[i]));
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                result.Add(new DiffLine(DiffLineKind.Removed, original[i]));
                i++;
            }
            else
            {
                result.Add(new DiffLine(DiffLineKind.Added, proposed[j]));
                j++;
            }
        }

        while (i < original.Length)
        {
            result.Add(new DiffLine(DiffLineKind.Removed, original[i]));
            i++;
        }

        while (j < proposed.Length)
        {
            result.Add(new DiffLine(DiffLineKind.Added, proposed[j]));
            j++;
        }
    }
}
