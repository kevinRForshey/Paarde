using System.Runtime.InteropServices;

namespace Paarde.Core.Helpers;

// Checks whether an executable would resolve on PATH, the same way the OS shell would find it,
// without actually starting a process (T6.1.3) - used as a cheap preflight before shelling out to
// `claude`, so a missing CLI can be reported with an actionable message instead of surfacing
// whatever exception Process.Start happens to throw.
public static class ExecutablePathResolver
{
    // pathVariable defaults to the real PATH environment variable; the parameter exists so tests
    // can point this at a temp directory instead of mutating process-wide state.
    public static bool ExistsOnPath(string executableName, string? pathVariable = null) =>
        ResolveFullPath(executableName, pathVariable) is not null;

    // Same search as ExistsOnPath, but returns the matched path (with whichever extension
    // resolved) instead of a bool - StdioLanguageServerClient needs the concrete path to decide
    // whether a server has to be launched through a shell (T18.x follow-up: a bare ".cmd"/".bat"
    // name, e.g. npm's typescript-language-server shim, is not directly executable via
    // Process.Start(UseShellExecute: false); CreateProcess only runs native images that way).
    public static string? ResolveFullPath(string executableName, string? pathVariable = null)
    {
        // Windows resolves a bare command name against these extensions in order (cmd.exe's
        // PATHEXT behavior); npm installs CLI shims as .cmd, so that has to be checked explicitly
        // rather than relying only on an exact, extension-less match like other platforms use.
        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { ".exe", ".cmd", ".bat", string.Empty }
            : new[] { string.Empty };

        // A name containing a path separator (an absolute path, or something like "./claude") is
        // used as-is rather than searched for on PATH, matching shell behavior - this lets T6.1.4's
        // configurable CLI path point at a specific file instead of only a bare command name.
        if (executableName.Contains(Path.DirectorySeparatorChar) || executableName.Contains(Path.AltDirectorySeparatorChar))
        {
            foreach (var extension in extensions)
            {
                if (File.Exists(executableName + extension))
                {
                    return executableName + extension;
                }
            }

            return null;
        }

        pathVariable ??= Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, executableName + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    // True for the Windows script shims (.cmd/.bat) that npm and similar tools install as a CLI's
    // launcher - these only run through a shell/batch interpreter, never directly via
    // Process.Start(UseShellExecute: false).
    public static bool RequiresShellWrapper(string resolvedFullPath) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
        (resolvedFullPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || resolvedFullPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));
}
