namespace Paarde.Core.Models;

// specs/editor-intelligence R1: mirrors LSP's hierarchical DocumentSymbol shape. GetDocumentSymbolsAsync
// returns a single synthetic root (Name = the file name, Children = the server's actual top-level
// symbols) rather than IReadOnlyList<LspDocumentSymbol>, so the outline TreeView and the breadcrumb
// computation (BreadcrumbPathBuilder) both walk one shape instead of special-casing "no parent at
// the top level." The root's own Range/SelectionRange are unused placeholders (zero-length at
// 0,0) - nothing ever jumps to the root itself.
public record LspDocumentSymbol(
    string Name,
    LspSymbolKind Kind,
    LspRange Range,
    LspRange SelectionRange,
    IReadOnlyList<LspDocumentSymbol> Children);
