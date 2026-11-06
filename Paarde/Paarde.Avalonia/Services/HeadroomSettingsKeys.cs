namespace Paarde.Avalonia.Services;

// Whether App.axaml.cs's startup hook should automatically run IHeadroomCoordinator against the
// saved working directory - off by default, same reasoning as AppearanceSettingsKeys
// .TransparencyEnabled: this can prompt to install software and spawn external processes, so it
// must never activate silently. The manual "Check now" button in Settings works regardless of
// this toggle, since that's an explicit user action.
public static class HeadroomSettingsKeys
{
    public const string IntegrationEnabled = "Headroom.IntegrationEnabled";

    // "Not now" on the automatic startup check's install/wiring prompt snoozes that prompt for a
    // while instead of re-asking on every launch - see HeadroomPromptSnoozeStore.
    public const string InstallPromptSnoozedUntilUtc = "Headroom.InstallPromptSnoozedUntilUtc";
    public const string WiringPromptSnoozedUntilUtc = "Headroom.WiringPromptSnoozedUntilUtc";
}
