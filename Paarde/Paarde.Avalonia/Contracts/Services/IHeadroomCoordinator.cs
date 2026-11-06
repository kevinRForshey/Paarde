using FluentAvalonia.UI.Controls;

namespace Paarde.Avalonia.Contracts.Services;

// Orchestrates IHeadroomService (Paarde.Core) + IDialogService to check whether the `headroom`
// CLI is installed, healthy, and the given project is durably wired for it, prompting (never
// forcing) the user through whatever's missing - the one place that logic lives, so the startup
// hook (App.axaml.cs) and the manual "Check now" button (SettingsViewModel) share identical
// behavior instead of duplicating the prompt sequencing.
public interface IHeadroomCoordinator
{
    // isAutomaticCheck distinguishes the unattended startup hook from the user explicitly
    // clicking "Check now": only the automatic path consults/sets prompt snoozing (see
    // IHeadroomPromptSnoozeStore) so it stops re-asking on every launch, while an explicit
    // manual check always prompts.
    Task<(string Message, InfoBarSeverity Severity)> CheckAndOfferFixesAsync(string projectPath, bool isAutomaticCheck, CancellationToken cancellationToken = default);
}
