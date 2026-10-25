namespace Paarde.Core.Models;

// Faithful, un-opinionated mirror of an LSP CompletionItem - the InsertText-falls-back-to-Label
// rule (R4.1 in specs/editor-autocomplete/requirements.md) is applied at the UI call site, not
// here, so this DTO stays a direct reflection of whatever the server actually sent. Kind is the
// raw LSP CompletionItemKind integer, carried through for a future icon pass (out of scope for v1
// - R3.4 only needs Detail as plain text).
public record LspCompletionItem(string Label, string? Detail, string? InsertText, int? Kind);
