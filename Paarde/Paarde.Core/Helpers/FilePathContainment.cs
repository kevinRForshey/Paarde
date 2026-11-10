namespace Paarde.Core.Helpers;

// Canonicalizes a tutorial-relative file path against the project's working directory and rejects
// anything that escapes it (path traversal, or an absolute/rooted LLM-supplied path) - extracted from
// FileChangeApplyService so ApplyOrchestrationService's pre-write conflict check
// can resolve the same path the write itself will use, rather than reading a file outside the
// project root before the write-time check would have caught it.
public static class FilePathContainment
{
    public static string Resolve(string workingDirectoryPath, string relativeFilePath)
    {
        if (string.IsNullOrWhiteSpace(workingDirectoryPath))
        {
            throw new InvalidOperationException("A project location must be selected before applying changes.");
        }

        var root = Path.GetFullPath(workingDirectoryPath);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativeFilePath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to write outside the project folder: '{relativeFilePath}' resolves to '{fullPath}'.");
        }

        return fullPath;
    }
}
