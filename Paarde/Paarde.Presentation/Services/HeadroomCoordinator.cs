using Paarde.Presentation.Models;

using Paarde.Presentation.Contracts.Services;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Presentation.Services;

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
    private readonly IHeadroomPromptSnoozeStore _promptSnoozeStore;

    public HeadroomCoordinator(IHeadroomService headroomService, IDialogService dialogService, IHeadroomPromptSnoozeStore promptSnoozeStore)
    {
        _headroomService = headroomService;
        _dialogService = dialogService;
        _promptSnoozeStore = promptSnoozeStore;
    }

    public async Task<(string Message, NotificationSeverity Severity)> CheckAndOfferFixesAsync(string projectPath, bool isAutomaticCheck, CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();
        var severity = NotificationSeverity.Success;

        if (!_headroomService.IsInstalled())
        {
            var (installed, installNote, installSeverity) = await TryInstallAsync(isAutomaticCheck, cancellationToken);
            notes.Add(installNote);
            Escalate(ref severity, installSeverity);

            if (!installed)
            {
                return (string.Join("\n", notes), severity);
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
            Escalate(ref severity, NotificationSeverity.Error);
        }
        else
        {
            notes.Add($"Headroom is working but not optimally configured:\n{DescribeWarnings(health.Checks)}");
            Escalate(ref severity, NotificationSeverity.Informational);
        }

        var (wiringNote, wiringSeverity) = await CheckProjectWiringAsync(projectPath, isAutomaticCheck, cancellationToken);
        notes.Add(wiringNote);
        Escalate(ref severity, wiringSeverity);

        return (string.Join("\n", notes), severity);
    }

    private async Task<(bool Installed, string Note, NotificationSeverity Severity)> TryInstallAsync(bool isAutomaticCheck, CancellationToken cancellationToken)
    {
        if (!_headroomService.IsUvAvailable())
        {
            return (false, "Headroom isn't installed, and uv isn't available to install it automatically.", NotificationSeverity.Warning);
        }

        if (isAutomaticCheck && await _promptSnoozeStore.IsSnoozedAsync(HeadroomPromptKind.Install))
        {
            return (false, "Headroom isn't installed - snoozed, use 'Check Headroom status now' to retry.", NotificationSeverity.Warning);
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "Install Headroom?",
            "Headroom (a token-savings proxy for Claude) isn't installed. Install it now via uv?",
            "Install",
            "Not now");

        if (!confirmed)
        {
            if (isAutomaticCheck)
            {
                await _promptSnoozeStore.SnoozeAsync(HeadroomPromptKind.Install);
            }

            return (false, "Headroom isn't installed - skipped installation.", NotificationSeverity.Warning);
        }

        var result = await _headroomService.InstallAsync(cancellationToken);

        return result.Succeeded
            ? (true, "Installed Headroom.", NotificationSeverity.Success)
            : (false, $"Failed to install Headroom (exit code {result.ExitCode}): {Truncate(result.StandardError)}", NotificationSeverity.Error);
    }

    private async Task<(string Note, NotificationSeverity Severity)> CheckProjectWiringAsync(string projectPath, bool isAutomaticCheck, CancellationToken cancellationToken)
    {
        if (await _headroomService.IsProjectWiredAsync(projectPath))
        {
            return ("This project already has durable Headroom wiring.", NotificationSeverity.Success);
        }

        if (isAutomaticCheck && await _promptSnoozeStore.IsSnoozedAsync(HeadroomPromptKind.Wiring))
        {
            return ("This project isn't wired for Headroom - snoozed, use 'Check Headroom status now' to retry.", NotificationSeverity.Warning);
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "Wire this project for Headroom?",
            "This project isn't yet wired for Headroom's token savings. Run 'headroom init claude' now?",
            "Run headroom init claude",
            "Not now");

        if (!confirmed)
        {
            if (isAutomaticCheck)
            {
                await _promptSnoozeStore.SnoozeAsync(HeadroomPromptKind.Wiring);
            }

            return ("Skipped wiring this project for Headroom.", NotificationSeverity.Warning);
        }

        var result = await _headroomService.RunInitClaudeAsync(projectPath, cancellationToken);

        return result.Succeeded
            ? ("Ran 'headroom init claude' - this project is now wired.", NotificationSeverity.Success)
            : ($"'headroom init claude' failed (exit code {result.ExitCode}): {Truncate(result.StandardError)}", NotificationSeverity.Error);
    }

    private static void Escalate(ref NotificationSeverity current, NotificationSeverity candidate)
    {
        if (Rank(candidate) > Rank(current))
        {
            current = candidate;
        }
    }

    // Avoids relying on NotificationSeverity's underlying int values (not documented as ordered by
    // severity) - this ranking is the one place "worse than" is defined for this flow.
    private static int Rank(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Error => 3,
        NotificationSeverity.Warning => 2,
        NotificationSeverity.Informational => 1,
        NotificationSeverity.Success => 0,
        _ => 1,
    };

    // exit_code 1 ("working but not optimal") covers things like no spend budget configured or
    // Claude Desktop bypassing the proxy - genuinely non-fatal, but not actionable from a bare
    // "run headroom doctor" message in a GUI app, so the actual check summaries are surfaced
    // instead of making the user open a terminal to find out what's warning. One line per check
    // (rather than joining with " | ") so this stays readable once hints make each entry longer
    // and there's more than one warning.
    private static string DescribeWarnings(IReadOnlyList<HeadroomCheckItem> checks)
    {
        var warnings = checks
            .Where(check => !string.Equals(check.Status, "pass", StringComparison.OrdinalIgnoreCase))
            .Select(check => Truncate(string.IsNullOrWhiteSpace(check.Hint)
                ? $"{check.Name}: {check.Summary}"
                : $"{check.Name}: {check.Summary} (hint: {check.Hint})", 120))
            .ToList();

        return warnings.Count > 0
            ? string.Join("\n", warnings.Select(warning => $"  - {warning}"))
            : "  - run 'headroom doctor' for details.";
    }

    private static string Truncate(string text, int maxLength = 200)
    {
        var trimmed = text.Trim();

        return trimmed.Length <= maxLength ? trimmed : string.Concat(trimmed.AsSpan(0, maxLength), "...");
    }
}
