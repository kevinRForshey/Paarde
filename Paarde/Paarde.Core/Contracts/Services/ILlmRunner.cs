using Paarde.Core.Models;

namespace Paarde.Core.Contracts.Services;

// Provider-agnostic abstraction over "send a prompt to an LLM and get a response".
// ClaudeCliRunner is the only implementation today; a direct Anthropic API runner is
// meant to be a second one behind the same interface, which is why nothing CLI-specific (an
// executable path, process arguments) appears here - each implementation owns resolving its own
// configuration.
public interface ILlmRunner
{
    // onTextDelta, if given, is invoked with each chunk of assistant text as the response streams
    // in - on a background thread for ClaudeCliRunner, so callers that touch UI state must marshal
    // themselves.
    //
    // onToolActivity, if given, is invoked with a short human-readable description
    // ("Reading `Foo.cs`") each time a read-only tool call completes - same background-thread
    // caveat as onTextDelta. Only ever fires when AgentToolsEnabled is on for whichever runner is
    // active; otherwise no tool is ever requested, so this is simply never called.
    //
    // resumeSessionId, if given, asks the runner to continue that earlier conversation
    // (the SessionId an earlier LlmRunResult returned) instead of starting a fresh one, so a
    // follow-up prompt carries the prior turn's context.
    Task<LlmRunResult> RunPromptAsync(string prompt, string? workingDirectory = null, Action<string>? onTextDelta = null, Action<string>? onToolActivity = null, string? resumeSessionId = null, CancellationToken cancellationToken = default);

    // Cheap preflight: lets callers report an unavailable runner with actionable guidance instead
    // of surfacing whatever exception the underlying call happens to throw.
    Task<bool> IsAvailableAsync();
}
