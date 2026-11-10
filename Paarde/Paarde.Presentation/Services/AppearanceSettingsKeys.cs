namespace Paarde.Presentation.Services;

// The Mica/Acrylic window backdrop is opt-in, not on by default, so
// this is read once at startup (App.axaml.cs) and whenever it's toggled on the Settings page
// (SettingsViewModel) - mirrors ClaudeCliSettingsKeys.
public static class AppearanceSettingsKeys
{
    public const string TransparencyEnabled = "Appearance.TransparencyEnabled";

    // Shared by every front end's ThemeSelectorService. The value predates
    // the "Appearance." prefix convention and is kept as-is so existing settings files keep loading.
    public const string RequestedTheme = "AppBackgroundRequestedTheme";
}
