namespace Paarde.Avalonia.Services;

// T8.2.3: persisted master-pane widths for every page with a resizable browse-list pane, keyed by
// page+pane so they can't collide - mirrors ClaudeCliSettingsKeys (T6.1.4) / WorkingDirectorySettingsKeys.
public static class WorkspaceLayoutSettingsKeys
{
    public const string ExplorerTreeWidth = "Layout.Explorer.TreeWidth";

    // Epic 25, T12: the docked Assistant panel's width on ExplorerView.
    public const string ExplorerAssistantPanelWidth = "Layout.Explorer.AssistantPanelWidth";

    // Epic 25, T11: ReviewPanelControl's own key - the permanent replacement for TutorialView's
    // sidebar, so it doesn't depend on a key named after the now-deleted TutorialView/DiffsView pages.
    public const string ReviewFileListWidth = "Layout.Review.FileListWidth";
    public const string HistorySessionListWidth = "Layout.History.SessionListWidth";
    public const string HistoryFileListWidth = "Layout.History.FileListWidth";

    // specs/editor-intelligence R4: the outline panel's width on each ExplorerFileTabControl -
    // shared by every open tab, same as every other pane width above.
    public const string OutlinePanelWidth = "Layout.Explorer.OutlinePanelWidth";

    // specs/editor-focus-layout R2: the secondary (split-right) TabView column's width.
    public const string ExplorerSplitGroupWidth = "Layout.Explorer.SplitGroupWidth";
}
