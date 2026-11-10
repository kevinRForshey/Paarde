namespace Paarde.Core.Models;

public record AnthropicModelInfo(string Id, string DisplayName);

// The models T6.3.3's Settings picker offers for AnthropicApiRunner - current as of this
// project's last update; Anthropic doesn't expose a "list models" endpoint that works with a plain
// API key, so this has to be a maintained list rather than something fetched at runtime.
public static class AnthropicModelCatalog
{
    public static readonly IReadOnlyList<AnthropicModelInfo> Models =
    [
        new("claude-opus-5", "Claude Opus 5"),
        new("claude-sonnet-5", "Claude Sonnet 5"),
        new("claude-fable-5-1", "Claude Fable 5.1"),
        new("claude-haiku-4-5-20251001", "Claude Haiku 4.5"),
    ];

    public const string DefaultModelId = "claude-sonnet-5";
}
