namespace Paarde.Presentation.Contracts.Services;

// Lets SettingsViewModel re-apply window-level appearance
// (today just the Epic 29 transparency/backdrop toggle) without reaching for the UI framework's
// main window itself.
public interface IWindowAppearanceService
{
    void ApplyTransparency(bool enabled);
}
