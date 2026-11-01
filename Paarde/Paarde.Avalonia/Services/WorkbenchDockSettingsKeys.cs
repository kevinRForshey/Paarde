namespace Paarde.Avalonia.Services;

// specs/ide-workbench-shell/design.md T6: a single app-wide dock state, not per-page/per-directory
// like WorkspaceLayoutSettingsKeys/RunConfigurationSettingsKeys - different enough shape to
// warrant its own small key file rather than folding into either.
public static class WorkbenchDockSettingsKeys
{
    public const string ActiveTab = "Dock.ActiveTab";

    public const string IsExpanded = "Dock.IsExpanded";
}
