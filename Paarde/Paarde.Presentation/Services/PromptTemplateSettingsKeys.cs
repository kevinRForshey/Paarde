namespace Paarde.Presentation.Services;

// Shared between SettingsViewModel (where the override is edited) and PromptViewModel (where it's
// read fresh on every run, so a change made here takes effect on the very next run without needing
// to revisit the Prompt page) - mirrors ClaudeCliSettingsKeys.
public static class PromptTemplateSettingsKeys
{
    // Blank/missing means "use the built-in default" (IPromptTemplateProvider's embedded template).
    public const string Override = "PromptTemplateOverride";
}
