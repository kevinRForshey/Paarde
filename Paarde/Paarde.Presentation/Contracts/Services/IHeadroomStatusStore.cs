using Paarde.Presentation.Models;

namespace Paarde.Presentation.Contracts.Services;

// Holds the most recent IHeadroomCoordinator result in memory so SettingsViewModel can show it
// when the user visits the Settings page, even though the automatic startup check (App.axaml.cs)
// runs before any page has navigated. Registered as a singleton so it survives page navigation;
// deliberately not persisted to disk - a stale result from a previous run isn't worth keeping,
// since the next startup (or a manual "Check now") always produces a fresh one.
//
// The startup check runs well after navigation to the first page (App.axaml.cs awaits it after
// NavigateTo), so a user who opens Settings before it finishes would otherwise see a stale/blank
// result forever - OnNavigatedTo only reads Message/Severity once, with nothing telling an
// already-displayed Settings page that a result arrived later. ResultChanged (mirroring
// IWorkingDirectoryContext.Changed's pub/sub pattern) lets SettingsViewModel update live instead.
public interface IHeadroomStatusStore
{
    string Message { get; }

    NotificationSeverity Severity { get; }

    event Action? ResultChanged;

    void SetResult(string message, NotificationSeverity severity);
}
