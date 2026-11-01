using System.Diagnostics;
using System.Text;

using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Paarde.Core.Services;

// Detects whether workingDirectoryPath sits inside a git working tree and, if so, which files are
// currently dirty there (T4.3.1), and stages files after a successful apply (T4.3.3) - both by
// shelling out to the git CLI rather than taking a LibGit2Sharp-style native dependency, matching
// the ClaudeCliRunner convention of treating an external CLI as the source of truth. Any failure to
// run git at all (not installed, directory missing, not a repository) is treated the same as "not a
// repository": this is an informational warning, not a security boundary, so it fails open rather
// than blocking an apply.
public class GitStatusService : IGitStatusService
{
    // T9.2.1: optional and defaulted to a no-op so every existing `new GitStatusService()` call
    // site (production DI registration, every test) keeps compiling unchanged - only the Avalonia
    // host, which actually configures a logging provider, needs to pass a real one.
    private readonly ILogger<GitStatusService> _logger;

    public GitStatusService(ILogger<GitStatusService>? logger = null)
    {
        _logger = logger ?? NullLogger<GitStatusService>.Instance;
    }

    public GitStatus GetStatus(string workingDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(workingDirectoryPath) || !Directory.Exists(workingDirectoryPath))
        {
            return GitStatus.NotARepository;
        }

        if (!TryRunGit(workingDirectoryPath, out var output))
        {
            return GitStatus.NotARepository;
        }

        var dirtyFiles = output
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 3)
            .Select(ParsePorcelainPath)
            .ToList();

        // R3.2: a failed branch lookup never blocks the dirty-file result above - same fail-open
        // posture as the rest of this class - it just leaves BranchName null.
        var branchName = RunGit(workingDirectoryPath, new List<string> { "branch", "--show-current" }, out var branchOutput, "run `git branch --show-current`")
            ? branchOutput.Trim()
            : string.Empty;

        return new GitStatus(IsRepository: true, BranchName: string.IsNullOrEmpty(branchName) ? null : branchName, DirtyFiles: dirtyFiles);
    }

    // Each porcelain line is "XY <path>", or "XY <path> -> <newPath>" for a rename; the path starts
    // after the two status characters and the space that follows them. git always reports the path
    // relative to the process's working directory (set below to workingDirectoryPath) using forward
    // slashes, the same convention ProposedFileChangeViewModel.FilePath already uses.
    private static string ParsePorcelainPath(string porcelainLine)
    {
        var path = porcelainLine[3..];
        var arrowIndex = path.IndexOf(" -> ", StringComparison.Ordinal);

        return arrowIndex >= 0 ? path[(arrowIndex + 4)..] : path;
    }

    public bool StageFiles(string workingDirectoryPath, IReadOnlyList<string> relativeFilePaths)
    {
        if (relativeFilePaths.Count == 0 || string.IsNullOrWhiteSpace(workingDirectoryPath) || !Directory.Exists(workingDirectoryPath))
        {
            return false;
        }

        // "--" stops option parsing, so a path that happens to start with "-" (unlikely, but an
        // LLM-supplied relative path isn't trustworthy by construction elsewhere in this codebase)
        // is never misread as a flag.
        var arguments = new List<string> { "add", "--" };
        arguments.AddRange(relativeFilePaths);

        return RunGit(workingDirectoryPath, arguments, out _, "stage files");
    }

    private bool TryRunGit(string workingDirectoryPath, out string output) =>
        RunGit(workingDirectoryPath, new List<string> { "status", "--porcelain" }, out output, "run `git status --porcelain`");

    private bool RunGit(string workingDirectoryPath, IReadOnlyList<string> arguments, out string output, string actionDescription)
    {
        output = string.Empty;

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return false;
            }

            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // git isn't on PATH, or the OS refused to start it - treated the same as "not a
            // repository" (see class remarks), but still worth a line in the log file since it's
            // otherwise silent.
            _logger.LogWarning(ex, "Failed to {Action} in {WorkingDirectory}; treating as not a git repository.", actionDescription, workingDirectoryPath);
            return false;
        }
    }
}
