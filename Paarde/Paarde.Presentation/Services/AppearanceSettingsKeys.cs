namespace Paarde.Presentation.Services;

// specs/fluent-design-pass R1: the Mica/Acrylic window backdrop is opt-in, not on by default, so
// this is read once at startup (App.axaml.cs) and whenever it's toggled on the Settings page
// (SettingsViewModel) - mirrors ClaudeCliSettingsKeys (T6.1.4).
public static class AppearanceSettingsKeys
{
    public const string TransparencyEnabled = "Appearance.TransparencyEnabled";

    // specs/winui-frontend R3.3: shared by every front end's ThemeSelectorService. The value predates
    // the "Appearance." prefix convention and is kept as-is so existing settings files keep loading.
    public const string RequestedTheme = "AppBackgroundRequestedTheme";
}
