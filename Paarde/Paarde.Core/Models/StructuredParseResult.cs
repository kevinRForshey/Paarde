namespace Paarde.Core.Models;

// Result of StructuredResponseParser.Parse. UsedFallback is true whenever the JSON
// contract (ClaudeJsonResponseParser) couldn't be parsed/validated and Response instead came from the
// legacy markdown parser (ClaudeResponseParser) as a best-effort last resort; ValidationError carries
// the JSON schema failure so a caller can retry once with it fed back to the model before falling
// back, per T5.1.2 - it's always null when UsedFallback is false.
public record StructuredParseResult(ClaudeParsedResponse Response, bool UsedFallback, string? ValidationError);
