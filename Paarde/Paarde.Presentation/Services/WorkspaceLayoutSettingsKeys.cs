namespace Paarde.Presentation.Services;

// Persisted master-pane widths for every page with a resizable browse-list pane, keyed by
// page+pane so they can't collide - mirrors ClaudeCliSettingsKeys / WorkingDirectorySettingsKeys.
public static class WorkspaceLayoutSettingsKeys
{
    public const string ExplorerTreeWidth = "Layout.Explorer.TreeWidth";

    // The docked Assistant panel's width on ExplorerView.
    public const string ExplorerAssistantPanelWidth = "Layout.Explorer.AssistantPanelWidth";

    // ReviewPanelControl's own key - the permanent replacement for TutorialView's
    // sidebar, so it doesn't depend on a key named after the now-deleted TutorialView/DiffsView pages.
    public const string ReviewFileListWidth = "Layout.Review.FileListWidth";
    public const string HistorySessionListWidth = "Layout.History.SessionListWidth";
    public const string HistoryFileListWidth = "Layout.History.FileListWidth";

    // The outline panel's width on each ExplorerFileTabControl -
    // shared by every open tab, same as every other pane width above.
    public const string OutlinePanelWidth = "Layout.Explorer.OutlinePanelWidth";

    // The secondary (split-right) TabView column's width.
    public const string ExplorerSplitGroupWidth = "Layout.Explorer.SplitGroupWidth";
}
