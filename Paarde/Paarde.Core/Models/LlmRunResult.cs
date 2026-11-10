namespace Paarde.Core.Models;

// Generic result of running a prompt through any ILlmRunner implementation. ExitCode is
// nullable because it's a CLI-process concept - a future direct-API runner has no
// equivalent and can leave it null; callers should branch on Success, not ExitCode.
public class LlmRunResult
{
    public bool Success { get; init; }

    public string Output { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    public int? ExitCode { get; init; }

    // Populated from the final "result" line of the CLI's stream-json output - null if that
    // line was never seen (e.g. the process was cancelled or killed before completing), or if the
    // runner implementation doesn't report usage.
    public double? TotalCostUsd { get; init; }

    public long? DurationMs { get; init; }

    public int? InputTokens { get; init; }

    public int? OutputTokens { get; init; }

    // Populated from the final "result" line's session_id - the id a later call can pass
    // back as resumeSessionId to continue this exact conversation. Null under the same conditions
    // as the other result-line fields above.
    public string? SessionId { get; init; }
}
