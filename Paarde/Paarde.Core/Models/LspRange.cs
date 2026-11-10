namespace Paarde.Core.Models;

// Mirrors LSP's Range directly (a Start/End LspPosition pair) - introduced alongside
// LspDocumentSymbol/LspHover since both need a start+end pair rather
// than LspDiagnostic's own ad-hoc Start/End fields.
public record LspRange(LspPosition Start, LspPosition End);
