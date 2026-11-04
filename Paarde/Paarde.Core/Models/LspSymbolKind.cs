namespace Paarde.Core.Models;

// Direct 1:1 mirror of LSP's numeric SymbolKind enum
// (https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#symbolKind)
// - a thin enum, not reused from any UI icon type, so Paarde.Core stays UI-framework-agnostic (same
// separation LspDiagnostic/LspCompletionItem already keep). Unknown = 0 is this DTO's own addition
// (not part of the LSP spec, which has no "unknown" value) - a safe fallback for a missing or
// unrecognized kind value rather than throwing while parsing.
public enum LspSymbolKind
{
    Unknown = 0,
    File = 1,
    Module = 2,
    Namespace = 3,
    Package = 4,
    Class = 5,
    Method = 6,
    Property = 7,
    Field = 8,
    Constructor = 9,
    Enum = 10,
    Interface = 11,
    Function = 12,
    Variable = 13,
    Constant = 14,
    String = 15,
    Number = 16,
    Boolean = 17,
    Array = 18,
    Object = 19,
    Key = 20,
    Null = 21,
    EnumMember = 22,
    Struct = 23,
    Event = 24,
    Operator = 25,
    TypeParameter = 26,
}
