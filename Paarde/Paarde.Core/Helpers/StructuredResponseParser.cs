using Paarde.Core.Models;

namespace Paarde.Core.Helpers;

// Entry point for parsing a Claude response under the Epic 5 JSON contract: prefer the structured
// ClaudeJsonResponseParser, and only fall back to the legacy markdown ClaudeResponseParser
// when the JSON contract couldn't be satisfied - surfacing why, so a caller can retry once
// before accepting the fallback as final.
public static class StructuredResponseParser
{
    public static StructuredParseResult Parse(string cliOutput, string? workingDirectoryPath = null)
    {
        if (ClaudeJsonResponseParser.TryParse(cliOutput, out var response, out var error, workingDirectoryPath))
        {
            return new StructuredParseResult(response!, UsedFallback: false, ValidationError: null);
        }

        var fallback = ClaudeResponseParser.Parse(cliOutput);
        return new StructuredParseResult(fallback, UsedFallback: true, ValidationError: error);
    }
}
