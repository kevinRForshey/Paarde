using FluentAvalonia.UI.Controls;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Services;

// Reimplements, as a native Paarde feature, the same check-and-offer-to-fix sequence already
// written and verified in the standalone headroom-check.sh/.ps1 scripts (~/LinuxSetUpScripts):
// confirm `headroom` is installed, report its health, and offer to durably wire the given
// project via `headroom init claude` - all through dialogs instead of a terminal prompt. Never
// throws and never forces anything: every mutating step (install, init claude) is gated behind
// an explicit IDialogService.ConfirmAsync, matching "optional helpers are never mandatory".
public class HeadroomCoordinator : IHeadroomCoordinator
{
    private readonly IHeadroomService _headroomService;
    private readonly IDialogService _dialogService;

    public HeadroomCoordinator(IHeadroomService headroomService, IDialogService dialogService)
    {
        _headroomService = headroomService;
        _dialogService = dialogService;
    }

    public async Task<(string Message, InfoBarSeverity Severity)> CheckAndOfferFixesAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();
        var severity = InfoBarSeverity.Success;

        if (!_headroomService.IsInstalled())
        {
            var (installed, installNote, installSeverity) = await TryInstallAsync(cancellationToken);
            notes.Add(installNote);
            Escalate(ref severity, installSeverity);

            if (!installed)
            {
                return (string.Join(" ", notes), severity);
            }
        }

        var health = await _headroomService.CheckHealthAsync(cancellationToken);

        if (health.IsHealthy)
        {
            notes.Add("Headroom is healthy.");
        }
        else if (health.IsDown)
        {
            notes.Add("Headroom's proxy/deployment appears to be down.");
            Escalate(ref severity, InfoBarSeverity.Error);
        }
        else
        {
            notes.Add($"Headroom is working but not optimally configured: {DescribeWarnings(health.Checks)}");
            Escalate(ref severity, InfoBarSeverity.Informational);
        }

        var (wiringNote, wiringSeverity) = await CheckProjectWiringAsync(projectPath, cancellationToken);
        notes.Add(wiringNote);
        Escalate(ref severity, wiringSeverity);

        return (string.Join(" ", notes), severity);
    }

    private async Task<(bool Installed, string Note, InfoBarSeverity Severity)> TryInstallAsync(CancellationToken cancellationToken)
    {
        if (!_headroomService.IsUvAvailable())
        {
            return (false, "Headroom isn't installed, and uv isn't available to install it automatically.", InfoBarSeverity.Warning);
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "Install Headroom?",
            "Headroom (a token-savings proxy for Claude) isn't installed. Install it now via uv?",
            "Install",
            "Not now");

        if (!confirmed)
        {
            return (false, "Headroom isn't installed - skipped installation.", InfoBarSeverity.Warning);
        }

        var result = await _headroomService.InstallAsync(cancellationToken);

        return result.Succeeded
            ? (true, "Installed Headroom.", InfoBarSeverity.Success)
            : (false, $"Failed to install Headroom (exit code {result.ExitCode}): {Truncate(result.StandardError)}", InfoBarSeverity.Error);
    }

    private async Task<(string Note, InfoBarSeverity Severity)> CheckProjectWiringAsync(string projectPath, CancellationToken cancellationToken)
    {
        if (await _headroomService.IsProjectWiredAsync(projectPath))
        {
            return ("This project already has durable Headroom wiring.", InfoBarSeverity.Success);
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "Wire this project for Headroom?",
            "This project isn't yet wired for Headroom's token savings. Run 'headroom init claude' now?",
            "Run headroom init claude",
            "Not now");

        if (!confirmed)
        {
            return ("Skipped wiring this project for Headroom.", InfoBarSeverity.Warning);
        }

        var result = await _headroomService.RunInitClaudeAsync(projectPath, cancellationToken);

        return result.Succeeded
            ? ("Ran 'headroom init claude' - this project is now wired.", InfoBarSeverity.Success)
            : ($"'headroom init claude' failed (exit code {result.ExitCode}): {Truncate(result.StandardError)}", InfoBarSeverity.Error);
    }

    private static void Escalate(ref InfoBarSeverity current, InfoBarSeverity candidate)
    {
        if (Rank(candidate) > Rank(current))
        {
            current = candidate;
        }
    }

    // Avoids relying on InfoBarSeverity's underlying int values (not documented as ordered by
    // severity) - this ranking is the one place "worse than" is defined for this flow.
    private static int Rank(InfoBarSeverity severity) => severity switch
    {
        InfoBarSeverity.Error => 3,
        InfoBarSeverity.Warning => 2,
        InfoBarSeverity.Informational => 1,
        InfoBarSeverity.Success => 0,
        _ => 1,
    };

    // exit_code 1 ("working but not optimal") covers things like no spend budget configured or
    // Claude Desktop bypassing the proxy - genuinely non-fatal, but not actionable from a bare
    // "run headroom doctor" message in a GUI app, so the actual check summaries are surfaced
    // instead of making the user open a terminal to find out what's warning.
    private static string DescribeWarnings(IReadOnlyList<HeadroomCheckItem> checks)
    {
        var warnings = checks
            .Where(check => !string.Equals(check.Status, "pass", StringComparison.OrdinalIgnoreCase))
            .Select(check => Truncate($"{check.Name}: {check.Summary}", 120))
            .ToList();

        return warnings.Count > 0 ? string.Join(" | ", warnings) : "run 'headroom doctor' for details.";
    }

    private static string Truncate(string text, int maxLength = 200)
    {
        var trimmed = text.Trim();

        return trimmed.Length <= maxLength ? trimmed : string.Concat(trimmed.AsSpan(0, maxLength), "...");
    }
}
