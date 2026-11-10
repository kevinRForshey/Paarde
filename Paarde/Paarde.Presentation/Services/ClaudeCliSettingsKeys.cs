namespace Paarde.Presentation.Services;

// Shared between SettingsViewModel (where these are edited) and PromptViewModel (where they're
// read to build the ClaudeCliOptions passed into each run) - kept in one place so the two can't
// drift apart.
public static class ClaudeCliSettingsKeys
{
    public const string ExecutablePath = "ClaudeCliExecutablePath";

    public const string Arguments = "ClaudeCliArguments";

    // Shared between both ILlmRunner implementations - one toggle controls
    // whether either runner offers the model read-only tools, regardless of which is selected.
    public const string AgentToolsEnabled = "ClaudeCliAgentToolsEnabled";
}
