namespace Paarde.Avalonia.Services;

// T8.2.3: persisted master-pane widths for every page with a resizable browse-list pane, keyed by
// page+pane so they can't collide - mirrors ClaudeCliSettingsKeys (T6.1.4) / WorkingDirectorySettingsKeys.
public static class WorkspaceLayoutSettingsKeys
{
    public const string ExplorerTreeWidth = "Layout.Explorer.TreeWidth";

    // Epic 25, T12: the docked Assistant panel's width on ExplorerView - DiffsFileListWidth/
    // TutorialFileListWidth are removed in T15 once DiffsView/TutorialView themselves are deleted.
    public const string ExplorerAssistantPanelWidth = "Layout.Explorer.AssistantPanelWidth";
    public const string DiffsFileListWidth = "Layout.Diffs.FileListWidth";
    public const string TutorialFileListWidth = "Layout.Tutorial.FileListWidth";
    public const string HistorySessionListWidth = "Layout.History.SessionListWidth";
    public const string HistoryFileListWidth = "Layout.History.FileListWidth";
}
