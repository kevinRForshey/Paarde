using System.Runtime.InteropServices;

namespace Paarde.Core.Helpers;

// RunConfigurationDetector builds BuildCommand/RunCommand strings that ProcessCommandRunner/
// PtyProcessManager later hand to a real shell (cmd.exe /c or /bin/sh -c, per design.md's
// "Relationship to Epic 12/16") - unlike those two classes' own command string, which is
// deliberately user-typed and trusted, the path interpolated here comes from
// Directory.EnumerateFiles and is only as trustworthy as the filesystem it's running against. A
// bare `"{path}"` wrap is exploitable on POSIX shells: a directory/file name containing an
// embedded `"` (illegal on Windows, legal on Linux/macOS) breaks out of the quoted argument and
// lets the rest of the name run as shell syntax. Wrapping in single quotes instead - the one POSIX
// quoting form with no exceptions except the quote character itself - closes that off completely,
// since `/bin/sh` does not expand `$`, backticks, or anything else inside single quotes.
public static class ShellArgumentQuoting
{
    public static string Quote(string argument) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"\"{argument}\""
            : $"'{argument.Replace("'", "'\\''")}'";
}
