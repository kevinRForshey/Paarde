namespace Paarde.Presentation.Services;

// Shared between SettingsViewModel (where these are edited) and PromptViewModel (where they're
// read to build the ClaudeCliOptions passed into each run) - kept in one place so the two can't
// drift apart (T6.1.4).
public static class ClaudeCliSettingsKeys
{
    public const string ExecutablePath = "ClaudeCliExecutablePath";

    public const string Arguments = "ClaudeCliArguments";

    // Epic 11 (F11.1.1): shared between both ILlmRunner implementations - one toggle controls
    // whether either runner offers the model read-only tools, regardless of which is selected.
    public const string AgentToolsEnabled = "ClaudeCliAgentToolsEnabled";
}
