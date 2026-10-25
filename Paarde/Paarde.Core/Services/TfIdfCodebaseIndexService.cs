using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.Models;

namespace Paarde.Core.Services;

// T14.2.1/T14.2.2: "suggest relevant files" without a cloud embeddings provider (none of
// Anthropic's APIs expose one) and without a vector DB - scores whole files via classic TF-IDF
// against the prompt's own text, keyed by working directory and invalidated per file on
// LastWriteTimeUtc rather than a full re-tokenize on every call. This is lexical, not semantic -
// it won't match a synonym or a paraphrase - but it needs no API key, no network call, and no new
// runtime dependency, which was the deciding tradeoff over a real embedding model.
//
// Deliberate simplification vs. the roadmap's original "chunk + embed" shape: scoring is per
// whole file, not per chunk, because the only thing a match is used for is attaching a whole file
// to the prompt (the same unit PromptViewModel.AttachedPaths already works in) - chunk-level
// granularity would have nowhere to surface.
public sealed class TfIdfCodebaseIndexService : ICodebaseIndexService
{
    // Skips huge files (generated/vendored blobs are the common case) - keeps indexing fast and
    // keeps one outlier file from dominating the term-frequency math.
    private const long MaxIndexableFileSizeBytes = 1024 * 1024;
    private const int MinTokenLength = 2;

    private static readonly Regex TokenPattern = new("[A-Za-z0-9]+", RegexOptions.Compiled);

    private sealed class FileTermVector
    {
        public required DateTime LastWriteTimeUtc { get; init; }
        public required Dictionary<string, int> TermCounts { get; init; }
        public required int TotalTerms { get; init; }
    }

    // Keyed by full working-directory path. No cross-directory eviction - acceptable since a
    // session typically has one project open at a time, and each entry's memory cost scales with
    // that project's own size, not with how many directories have ever been indexed.
    private readonly ConcurrentDictionary<string, Dictionary<string, FileTermVector>> _indexesByWorkingDirectory = new();

    public Task<IReadOnlyList<RelevantFileMatch>> FindRelevantFilesAsync(string workingDirectoryPath, string queryText, int maxResults = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workingDirectoryPath) || !Directory.Exists(workingDirectoryPath) || string.IsNullOrWhiteSpace(queryText))
        {
            return Task.FromResult<IReadOnlyList<RelevantFileMatch>>([]);
        }

        var fullWorkingDirectoryPath = Path.GetFullPath(workingDirectoryPath);
        var index = _indexesByWorkingDirectory.GetOrAdd(fullWorkingDirectoryPath, static _ => new Dictionary<string, FileTermVector>());

        RefreshIndex(fullWorkingDirectoryPath, index, cancellationToken);

        var queryTerms = Tokenize(queryText).Distinct().ToList();
        if (queryTerms.Count == 0 || index.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RelevantFileMatch>>([]);
        }

        var documentCount = index.Count;
        var documentFrequency = queryTerms.ToDictionary(
            term => term,
            term => index.Values.Count(vector => vector.TermCounts.ContainsKey(term)));

        var matches = new List<RelevantFileMatch>();
        foreach (var (filePath, vector) in index)
        {
            var score = 0.0;
            foreach (var term in queryTerms)
            {
                if (!vector.TermCounts.TryGetValue(term, out var termCount))
                {
                    continue;
                }

                var termFrequency = (double)termCount / vector.TotalTerms;
                var inverseDocumentFrequency = Math.Log((double)(documentCount + 1) / (documentFrequency[term] + 1)) + 1;
                score += termFrequency * inverseDocumentFrequency;
            }

            if (score > 0)
            {
                matches.Add(new RelevantFileMatch(ToRelativePath(fullWorkingDirectoryPath, filePath), score));
            }
        }

        IReadOnlyList<RelevantFileMatch> ranked = matches
            .OrderByDescending(match => match.Score)
            .Take(maxResults)
            .ToList();

        return Task.FromResult(ranked);
    }

    private static void RefreshIndex(string fullWorkingDirectoryPath, Dictionary<string, FileTermVector> index, CancellationToken cancellationToken)
    {
        var tree = FileTreeBuilder.Build(fullWorkingDirectoryPath);
        var currentFilePaths = new HashSet<string>();

        foreach (var filePath in FlattenFiles(tree))
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentFilePaths.Add(filePath);

            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
            if (index.TryGetValue(filePath, out var cached) && cached.LastWriteTimeUtc == lastWriteTimeUtc)
            {
                continue;
            }

            var termCounts = TokenizeFile(filePath);
            if (termCounts is null)
            {
                index.Remove(filePath);
                continue;
            }

            index[filePath] = new FileTermVector
            {
                LastWriteTimeUtc = lastWriteTimeUtc,
                TermCounts = termCounts,
                TotalTerms = termCounts.Values.Sum(),
            };
        }

        // Drops files that were deleted, or became gitignored, since the last refresh.
        foreach (var stalePath in index.Keys.Where(path => !currentFilePaths.Contains(path)).ToList())
        {
            index.Remove(stalePath);
        }
    }

    private static Dictionary<string, int>? TokenizeFile(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > MaxIndexableFileSizeBytes)
            {
                return null;
            }

            if (fileInfo.Length == 0)
            {
                return new Dictionary<string, int>();
            }

            var bytes = File.ReadAllBytes(filePath);
            if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8000)) >= 0)
            {
                return null; // binary heuristic: a NUL byte in the first 8KB
            }

            var termCounts = new Dictionary<string, int>();
            foreach (var term in Tokenize(Encoding.UTF8.GetString(bytes)))
            {
                termCounts[term] = termCounts.GetValueOrDefault(term) + 1;
            }

            return termCounts;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> Tokenize(string text) =>
        TokenPattern.Matches(text)
            .Select(match => match.Value.ToLowerInvariant())
            .Where(token => token.Length >= MinTokenLength);

    private static IEnumerable<string> FlattenFiles(FileTreeEntry entry)
    {
        if (!entry.IsDirectory)
        {
            yield return entry.FullPath;
            yield break;
        }

        foreach (var child in entry.Children)
        {
            foreach (var descendant in FlattenFiles(child))
            {
                yield return descendant;
            }
        }
    }

    private static string ToRelativePath(string workingDirectoryPath, string filePath) =>
        Path.GetRelativePath(workingDirectoryPath, filePath).Replace(Path.DirectorySeparatorChar, '/');
}
