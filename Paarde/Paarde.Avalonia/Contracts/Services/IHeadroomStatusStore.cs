using FluentAvalonia.UI.Controls;

namespace Paarde.Avalonia.Contracts.Services;

// Holds the most recent IHeadroomCoordinator result in memory so SettingsViewModel can show it
// when the user visits the Settings page, even though the automatic startup check (App.axaml.cs)
// runs before any page has navigated. Registered as a singleton so it survives page navigation;
// deliberately not persisted to disk - a stale result from a previous run isn't worth keeping,
// since the next startup (or a manual "Check now") always produces a fresh one.
public interface IHeadroomStatusStore
{
    string Message { get; }

    InfoBarSeverity Severity { get; }

    void SetResult(string message, InfoBarSeverity severity);
}
