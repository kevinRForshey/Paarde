namespace Paarde.Core.Models;

// Arguments is split on whitespace when building the process's argument list - there's no
// quoting support, so an argument value that itself needs to contain a space isn't representable.
//
// AgentToolsEnabled gates whether RunPromptAsync appends --allowedTools/
// --permission-mode - off by default, since this changes what the model can *do* mid-run, not
// just how it's invoked.
public record ClaudeCliOptions(string ExecutablePath, string Arguments, bool AgentToolsEnabled = false)
{
    public static readonly ClaudeCliOptions Default = new("claude", "-p", false);
}
