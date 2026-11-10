using Paarde.Core.Models;

namespace Paarde.Core.Helpers;

// Builds a FileTreeEntry tree of a working directory for the Explorer page, honoring the
// project's root `.gitignore` via GitIgnoreMatcher (the same matcher ProtectedPathChecker uses, so
// "ignored" means the same thing everywhere in the app) - ".git" itself is always excluded too,
// since it's noise in a file browser regardless of whether it's been gitignored. Directories are
// listed before files, both alphabetically, matching the usual IDE explorer convention.
public static class FileTreeBuilder
{
    public static FileTreeEntry Build(string rootPath)
    {
        var fullRootPath = Path.GetFullPath(rootPath);
        return BuildDirectory(fullRootPath, Path.GetFileName(fullRootPath.TrimEnd(Path.DirectorySeparatorChar)), fullRootPath);
    }

    private static FileTreeEntry BuildDirectory(string directoryPath, string name, string rootPath)
    {
        var children = new List<FileTreeEntry>();

        string[] entryPaths;
        try
        {
            entryPaths = Directory.GetFileSystemEntries(directoryPath);
        }
        catch (IOException)
        {
            entryPaths = [];
        }
        catch (UnauthorizedAccessException)
        {
            entryPaths = [];
        }

        foreach (var entryPath in entryPaths)
        {
            var entryName = Path.GetFileName(entryPath);
            if (entryName == ".git")
            {
                continue;
            }

            var isDirectory = Directory.Exists(entryPath);
            var relativePath = Path.GetRelativePath(rootPath, entryPath).Replace(Path.DirectorySeparatorChar, '/');

            if (GitIgnoreMatcher.IsIgnored(rootPath, relativePath))
            {
                continue;
            }

            children.Add(isDirectory
                ? BuildDirectory(entryPath, entryName, rootPath)
                : new FileTreeEntry(entryName, entryPath, IsDirectory: false, Children: []));
        }

        var orderedChildren = children
            .OrderByDescending(static c => c.IsDirectory)
            .ThenBy(static c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FileTreeEntry(name, directoryPath, IsDirectory: true, Children: orderedChildren);
    }
}
