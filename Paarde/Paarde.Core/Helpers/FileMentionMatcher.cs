using Paarde.Core.Models;

namespace Paarde.Core.Helpers;

// Backs the prompt box's `@`-triggered autocomplete popup. Operates on a FileTreeEntry
// tree (the same shape FileTreeBuilder.Build already produces for Explorer, T8.1.1) rather than
// walking the filesystem itself, so it stays gitignore-agnostic (whatever tree it's given is
// already filtered) and trivially testable against an in-memory fake tree - no temp directories
// needed.
public static class FileMentionMatcher
{
    private const int DefaultMaxResults = 20;

    public static IReadOnlyList<string> FindMatches(FileTreeEntry root, string query, int maxResults = DefaultMaxResults)
    {
        var relativePaths = Flatten(root)
            .Where(entry => !entry.IsDirectory)
            .Select(entry => ToRelativePath(root, entry));

        if (!string.IsNullOrEmpty(query))
        {
            relativePaths = relativePaths.Where(path => path.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return relativePaths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    private static IEnumerable<FileTreeEntry> Flatten(FileTreeEntry entry)
    {
        if (!entry.IsDirectory)
        {
            yield return entry;
            yield break;
        }

        foreach (var child in entry.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static string ToRelativePath(FileTreeEntry root, FileTreeEntry file) =>
        Path.GetRelativePath(root.FullPath, file.FullPath).Replace(Path.DirectorySeparatorChar, '/');
}
