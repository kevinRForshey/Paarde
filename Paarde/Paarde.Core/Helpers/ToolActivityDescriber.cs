using Newtonsoft.Json.Linq;

namespace Paarde.Core.Helpers;

// Turns one read-only tool call into a short, human-readable description for
// ILlmRunner's onToolActivity callback. Shared between ClaudeCliRunner (whose tool names/fields
// come straight from the CLI's own built-in Read/Grep/Glob tools) and AnthropicApiRunner (whose
// read_file/grep/glob tool schemas are this app's own, with different names and field names) so
// both runners describe the same three operations the same way regardless of which is active.
public static class ToolActivityDescriber
{
    public static string Describe(string toolName, JObject? input)
    {
        var path = input?["file_path"]?.ToString() ?? input?["path"]?.ToString();
        var pattern = input?["pattern"]?.ToString();

        return toolName.ToLowerInvariant() switch
        {
            "read" or "read_file" => $"Reading `{path}`",
            "grep" => $"Searching for `{pattern}`",
            "glob" => $"Listing `{pattern}`",
            _ => $"Using tool `{toolName}`",
        };
    }
}
