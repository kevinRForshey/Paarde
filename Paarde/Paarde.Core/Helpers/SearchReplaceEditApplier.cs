using Paarde.Core.Models;

namespace Paarde.Core.Helpers;

// Applies the F5.2 search/replace edit blocks (ClaudeJsonEdit) against a file's real current content,
// so the model can send only the changed lines instead of echoing the whole file. Tries
// progressively fuzzier line-matching strategies before giving up on an edit. An edit whose
// "search" text can't be located under any strategy - including because it matches more than once,
// which is just as unusable as not matching at all - is reported as failed rather than guessed at;
// callers (see ClaudeJsonResponseParser) are expected to treat any failed edit as reason to reject the
// whole response, not silently apply the edits that did succeed, since there would be no way
// to tell a step that was skipped because it failed apart from one that never needed to touch the file.
public static class SearchReplaceEditApplier
{
    private static readonly Func<string, string>[] NormalizationStrategies =
    [
        line => line,
        line => line.Trim(),
        line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
    ];

    public static EditApplicationResult Apply(string originalContent, IReadOnlyList<ClaudeJsonEdit> edits)
    {
        var lines = SplitLines(originalContent).ToList();
        var outcomes = new List<EditOutcome>(edits.Count);

        foreach (var edit in edits)
        {
            var searchLines = SplitLines(edit.Search ?? string.Empty);
            var location = Locate(lines, searchLines);

            if (location is null)
            {
                outcomes.Add(new EditOutcome(false, null));
                continue;
            }

            var (startLine, lineCount) = location.Value;
            var replaceLines = SplitLines(edit.Replace ?? string.Empty);

            lines.RemoveRange(startLine, lineCount);
            lines.InsertRange(startLine, replaceLines);

            var resultLocation = replaceLines.Length > 0
                ? new HunkLocation(startLine, startLine + replaceLines.Length - 1)
                : null;

            outcomes.Add(new EditOutcome(true, resultLocation));
        }

        return new EditApplicationResult(string.Join('\n', lines), outcomes);
    }

    // Tries exact matching first, then line-by-line with surrounding whitespace trimmed (handles the
    // model reproducing different indentation), then with internal whitespace runs collapsed too
    // (handles reformatted spacing within a line). A strategy that matches more than once is treated
    // the same as one that matches zero times - rather than guessing which occurrence was intended -
    // and the next, fuzzier strategy is tried anyway since it can only ever match a superset of lines,
    // so it would be ambiguous too and the search simply ends unresolved.
    private static (int StartLine, int LineCount)? Locate(List<string> contentLines, string[] searchLines)
    {
        if (searchLines.Length == 0 || searchLines.Length > contentLines.Count)
        {
            return null;
        }

        foreach (var normalize in NormalizationStrategies)
        {
            var match = LocateWithNormalization(contentLines, searchLines, normalize);

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static (int StartLine, int LineCount)? LocateWithNormalization(List<string> contentLines, string[] searchLines, Func<string, string> normalize)
    {
        var normalizedSearchLines = searchLines.Select(normalize).ToArray();
        int? found = null;

        for (var start = 0; start <= contentLines.Count - searchLines.Length; start++)
        {
            var isMatch = true;

            for (var offset = 0; offset < searchLines.Length; offset++)
            {
                if (normalize(contentLines[start + offset]) != normalizedSearchLines[offset])
                {
                    isMatch = false;
                    break;
                }
            }

            if (!isMatch)
            {
                continue;
            }

            if (found.HasValue)
            {
                return null;
            }

            found = start;
        }

        return found.HasValue ? (found.Value, searchLines.Length) : null;
    }

    private static string[] SplitLines(string text) =>
        string.IsNullOrEmpty(text) ? [] : text.Replace("\r\n", "\n").Split('\n');
}

// Outcome of one ClaudeJsonEdit within Apply's `edits` list, in the same order. Location is the
// resolved line range of its replacement text within the result content, or null when the edit either
// failed (Success is false) or replaced its search text with nothing (a pure deletion has nowhere
// meaningful to point a step at).
public record EditOutcome(bool Success, HunkLocation? Location);

public record EditApplicationResult(string Content, IReadOnlyList<EditOutcome> Outcomes);
