namespace Paarde.Presentation.Models;

/// <summary>
/// UI-framework-agnostic app theme choice. Maps to Avalonia's <c>ThemeVariant</c> and WinUI 3's
/// <c>ElementTheme</c>; <see cref="Default"/> follows the OS setting. Member names are also the
/// persisted setting values, so they must not be renamed.
/// </summary>
public enum AppTheme
{
    Default,
    Light,
    Dark,
}
