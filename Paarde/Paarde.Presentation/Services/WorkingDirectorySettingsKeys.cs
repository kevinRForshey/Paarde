namespace Paarde.Presentation.Services;

// Shared between PromptViewModel (where the working directory is picked/persisted) and
// ExplorerViewModel (where it's read to know which directory to show a file tree of, T8.1.1) -
// kept in one place so the two can't drift apart, mirroring ClaudeCliSettingsKeys.
public static class WorkingDirectorySettingsKeys
{
    public const string WorkingDirectory = "SavedWorkingDirectory";
}
