using System.Text;

namespace Paarde.Core.Models;

// What FileChangeApplyService.Apply needs to round-trip a file's on-disk byte format instead of
// always normalizing to UTF-8 (no BOM) with whatever line endings happened to be in the LLM's
// proposed content. NewLine is null when there's nothing to preserve - a brand-new file,
// or an existing one with no newlines at all - in which case Apply leaves the composed content's
// line endings untouched.
public sealed record FileEncodingInfo(Encoding Encoding, string? NewLine)
{
    // Used for a file that doesn't exist on disk yet (the proposal creates it) - nothing to
    // preserve, so this matches the encoding File.WriteAllText used before this existed.
    public static readonly FileEncodingInfo Default = new(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), null);
}
