using Paarde.Core.Models;

namespace Paarde.Core.Contracts.Services;

public interface IGitStatusService
{
    // Shells out to the git CLI to check whether workingDirectoryPath is inside a git working tree
    // and, if so, which files are dirty there. Returns GitStatus.NotARepository - never
    // throws - if git isn't installed, the directory doesn't exist, or it isn't a repository; this
    // is an informational warning, not a security boundary, so a missing/failed git command should
    // never block an apply.
    GitStatus GetStatus(string workingDirectoryPath);

    // Stages relativeFilePaths (git add) in workingDirectoryPath after a successful apply,
    // so the user's next `git status`/`git diff --staged` already reflects what Paarde just wrote
    // without an extra manual step. Only touches the index, never the working tree or history, so
    // it's as reversible as any other `git add` (`git restore --staged`) - never throws; returns
    // false (and stages nothing) if relativeFilePaths is empty, workingDirectoryPath isn't a git
    // repository, or git isn't installed, the same fail-open posture as GetStatus.
    bool StageFiles(string workingDirectoryPath, IReadOnlyList<string> relativeFilePaths);
}
