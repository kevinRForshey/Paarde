using Paarde.Core.Models;

namespace Paarde.Core.Contracts.Services;

// Talks to the `headroom` CLI (https://github.com - the token-savings proxy/wrapper tool) so
// Paarde can check, and offer to fix, whether it's installed, healthy, and wired for the
// currently-open project. Pure CLI/filesystem concerns only - no dialogs, no settings, no
// InfoBarSeverity - IHeadroomCoordinator (Paarde.Avalonia) owns the prompt/sequencing logic that
// decides when to call these.
public interface IHeadroomService
{
    // Whether `headroom` resolves to a file, either on PATH or in the well-known fallback
    // locations HeadroomService also checks (e.g. ~/.local/bin, where pip --user/uv tool install
    // put it) - a cheap preflight before shelling out.
    bool IsInstalled();

    // Whether `uv` resolves the same way IsInstalled does, needed before InstallAsync can do
    // anything.
    bool IsUvAvailable();

    // Runs `headroom doctor --json` and parses its exit_code/checks[]. Never throws: unparseable
    // or missing output comes back as HeadroomHealthResult.ExitCode == null (IsDown == true).
    Task<HeadroomHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default);

    // Reads {projectPath}/.claude/settings.local.json and settings.json (first one found wins)
    // for an existing SessionStart hook whose command contains "--marker headroom-init-claude"
    // (installed by `headroom init claude`) or "--marker headroom-wrap-selfheal" (installed by
    // `headroom wrap claude`) - either means the project already has some form of Headroom
    // session wiring. Never throws: a missing/malformed file or missing project directory just
    // means "not wired" (false).
    Task<bool> IsProjectWiredAsync(string projectPath);

    // Runs `uv tool install "headroom-ai[all]"` - uv manages its own isolated venv for the tool,
    // so this works even on distros (e.g. Arch, Debian) whose system Python is "externally
    // managed" (PEP 668) and refuses a plain `pip install`.
    Task<CommandResult> InstallAsync(CancellationToken cancellationToken = default);

    // Runs `headroom init claude` with projectPath as the working directory - defers the actual
    // hook/settings merge to Headroom's own installer rather than hand-rolling it here.
    Task<CommandResult> RunInitClaudeAsync(string projectPath, CancellationToken cancellationToken = default);
}
