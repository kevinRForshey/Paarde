namespace Paarde.Core.Helpers;

// Decides whether a proposed write's target is somewhere an apply should refuse to touch without
// explicit confirmation: inside `.git/`, `bin/`, or `obj/`, or matched by the project's
// `.gitignore`. Deliberately separate from FilePathContainment - that check is about
// writes escaping the working directory entirely (a security boundary that's never bypassable);
// this one is about writes landing somewhere that's merely unwise by default (build output,
// VCS internals, explicitly-ignored files), which the user can choose to override per apply.
public static class ProtectedPathChecker
{
    private static readonly string[] ProtectedSegments = { ".git", "bin", "obj" };

    public static bool IsProtected(string workingDirectoryPath, string relativeFilePath)
    {
        var normalizedPath = relativeFilePath.Replace('\\', '/').TrimStart('/');
        var segments = normalizedPath.Split('/');

        if (segments.Take(segments.Length - 1).Any(segment => ProtectedSegments.Contains(segment)))
        {
            return true;
        }

        return GitIgnoreMatcher.IsIgnored(workingDirectoryPath, relativeFilePath);
    }
}
