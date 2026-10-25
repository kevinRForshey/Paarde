namespace Paarde.Core.Models;

// Zero-based line/character, matching the LSP spec's `Position` directly - conversion to/from
// AvaloniaEdit's document-offset model happens at the CodeEditor boundary, not here.
public record LspPosition(int Line, int Character);
