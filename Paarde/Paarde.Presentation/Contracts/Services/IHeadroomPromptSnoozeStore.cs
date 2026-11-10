namespace Paarde.Presentation.Contracts.Services;

public enum HeadroomPromptKind
{
    Install,
    Wiring,
}

// Lets the automatic startup check remember "the user already said not now" for a prompt kind,
// so IHeadroomCoordinator doesn't pop the same confirm dialog on every single app launch. Only
// consulted/set for the automatic startup path - the manual "Check now" button is an explicit
// user action and always prompts regardless of snooze state.
public interface IHeadroomPromptSnoozeStore
{
    Task<bool> IsSnoozedAsync(HeadroomPromptKind kind);

    Task SnoozeAsync(HeadroomPromptKind kind);
}
