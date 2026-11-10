namespace Paarde.Core.Models;

// Contents is pre-flattened to a single string at the client
// boundary (joining LSP's MarkupContent/MarkedString[] variants) - CodeEditor renders it as
// plain/wrapped text in a tooltip, not rendered Markdown, so there's no reason to carry the raw
// union type past StdioLanguageServerClient.
public record LspHover(string Contents, LspRange? Range);
