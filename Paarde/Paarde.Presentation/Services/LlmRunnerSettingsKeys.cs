namespace Paarde.Presentation.Services;

// Shared between SettingsViewModel (where this is edited) and LlmRunnerSelector (where it's read to
// decide which concrete ILlmRunner to delegate to).
public static class LlmRunnerSettingsKeys
{
    public const string SelectedRunner = "LlmRunnerSelectedKind";
}
