using System.Text;
using System.Text.RegularExpressions;

namespace Paarde.Core.Helpers;

// Minimal .gitignore matcher, used by ProtectedPathChecker and FileTreeBuilder -
// reads a single top-level `.gitignore` from the project's working directory and matches a
// relative file path against it. Deliberately scoped to the common subset real-world .gitignore
// files actually use: comments ('#'), blank lines, '!' negation (later lines override earlier
// ones, same precedence as git), a leading '/' anchoring a pattern to the root, a trailing '/'
// marking a directory pattern, '*'/'**'/'?' wildcards, and '[...]' character classes (needed for
// this very repo's own .gitignore, which uses '[Bb]in/'-style case-insensitive patterns). Does NOT
// support nested per-directory .gitignore files, a global/user-level gitignore, or
// `.git/info/exclude` - git's full ignore semantics are considerably larger than either consumer
// needs, and this covers every pattern shape in a typical generated .gitignore (Visual Studio's,
// dotnet's, etc.).
public static class GitIgnoreMatcher
{
    public static bool IsIgnored(string workingDirectoryPath, string relativeFilePath)
    {
        var gitIgnorePath = Path.Combine(workingDirectoryPath, ".gitignore");

        if (!File.Exists(gitIgnorePath))
        {
            return false;
        }

        string[] lines;

        try
        {
            lines = File.ReadAllLines(gitIgnorePath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        var normalizedPath = relativeFilePath.Replace('\\', '/').TrimStart('/');
        var ignored = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var negate = line.StartsWith('!');
            if (negate)
            {
                line = line[1..];
            }

            // Later lines win over earlier ones regardless of direction (ignore then un-ignore, or
            // vice versa) - matches git's own last-match-wins precedence.
            if (line.Length > 0 && IsMatch(line, normalizedPath))
            {
                ignored = !negate;
            }
        }

        return ignored;
    }

    private static bool IsMatch(string pattern, string path)
    {
        var anchoredToRoot = pattern.StartsWith('/');
        if (anchoredToRoot)
        {
            pattern = pattern[1..];
        }

        // A trailing '/' just marks "this is meant to be a directory" - stripped before building
        // the regex. It doesn't change matching here: git recursively ignores everything under a
        // matched directory regardless of whether the pattern that matched it had a trailing
        // slash, so every pattern already gets that treatment below (checked against every
        // ancestor prefix of the path, not just the full path).
        pattern = pattern.TrimEnd('/');

        var regex = BuildRegex(pattern, anchoredToRoot);
        var segments = path.Split('/');

        for (var depth = 1; depth <= segments.Length; depth++)
        {
            var prefix = string.Join('/', segments.Take(depth));

            if (regex.IsMatch(prefix))
            {
                return true;
            }
        }

        return false;
    }

    private static Regex BuildRegex(string pattern, bool anchoredToRoot)
    {
        var regexText = new StringBuilder();

        // Anchored: must match from the very start of the path. Unanchored: matches starting at
        // any path component, not just the root - "bin" should also catch "src/bin".
        regexText.Append(anchoredToRoot ? "^" : "(^|.*/)");

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];

            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                regexText.Append(".*");
                i++;
            }
            else if (c == '*')
            {
                regexText.Append("[^/]*");
            }
            else if (c == '?')
            {
                regexText.Append("[^/]");
            }
            else if (c == '[')
            {
                var closingBracket = pattern.IndexOf(']', i + 1);
                if (closingBracket < 0)
                {
                    regexText.Append(Regex.Escape(c.ToString()));
                }
                else
                {
                    // Passed through as-is (minus the brackets themselves) rather than escaped - a
                    // gitignore character class like "[Dd]" or "[a-z]" already matches regex's own
                    // '[...]' syntax.
                    regexText.Append('[').Append(pattern.AsSpan(i + 1, closingBracket - i - 1)).Append(']');
                    i = closingBracket;
                }
            }
            else
            {
                regexText.Append(Regex.Escape(c.ToString()));
            }
        }

        regexText.Append('$');
        return new Regex(regexText.ToString());
    }
}
