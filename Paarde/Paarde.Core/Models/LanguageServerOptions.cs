namespace Paarde.Core.Models;

// Epic 18: per-language counterpart to Epic 17's single global LspOptions - code intelligence now
// covers more than one language, so each gets its own executable/arguments pair instead of one
// global pair. Arguments is split on whitespace when building the process's argument list, same
// limitation as ClaudeCliOptions.Arguments/the old LspOptions.
public record LanguageServerOptions(string LanguageId, string ExecutablePath, string Arguments, bool ForceEnabled = false)
{
    // specs/multi-language-support/design.md: pyright over python-lsp-server for Python (best
    // out-of-box type-checking signal, no plugin setup needed); typescript-language-server for
    // TypeScript, since it already handles .ts/.tsx/.js/.jsx through one process - "React" is not
    // a distinct LSP target.
    public static readonly IReadOnlyList<LanguageServerOptions> Defaults =
    [
        new LanguageServerOptions("csharp", "csharp-ls", string.Empty),
        new LanguageServerOptions("python", "pyright-langserver", "--stdio"),
        new LanguageServerOptions("typescript", "typescript-language-server", "--stdio"),
    ];
}
