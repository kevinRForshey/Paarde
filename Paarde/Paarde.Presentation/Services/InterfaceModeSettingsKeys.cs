namespace Paarde.Presentation.Services;

// Shared between SettingsViewModel (where this is edited) and App.axaml.cs/ShellView.axaml.cs/
// ExplorerView.axaml.cs/AssistantPanelViewModel (where it's read to decide startup navigation and
// layout).
public static class InterfaceModeSettingsKeys
{
    public const string Mode = "Interface.Mode";
}
