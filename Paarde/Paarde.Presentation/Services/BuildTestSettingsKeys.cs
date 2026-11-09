namespace Paarde.Presentation.Services;

// Shared between SettingsViewModel (where these are edited) and TutorialViewModel (where they're
// read after an apply, Epic 12 F12.2) - same one-place-so-they-can't-drift-apart rationale as
// ClaudeCliSettingsKeys.
public static class BuildTestSettingsKeys
{
    public const string BuildCommand = "BuildTestBuildCommand";

    public const string TestCommand = "BuildTestTestCommand";
}
