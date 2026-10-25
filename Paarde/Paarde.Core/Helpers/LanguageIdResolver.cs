namespace Paarde.Core.Helpers;

// Epic 18 (specs/multi-language-support): maps a file's extension to the LSP languageId used both
// to pick which language server a file's notifications/requests route to
// (LanguageServerSessionManager) and to populate textDocument/didOpen's own "languageId" field
// (StdioLanguageServerClient), replacing the latter's old hardcoded "csharp" literal. Shared by
// the forthcoming editor-autocomplete spec's completion routing too - kept as its own small static
// helper rather than inlined in either caller.
public static class LanguageIdResolver
{
    private static readonly Dictionary<string, string> ExtensionToLanguageId = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "csharp",
        [".py"] = "python",
        [".ts"] = "typescript",
        [".tsx"] = "typescriptreact",
        [".js"] = "javascript",
        [".mjs"] = "javascript",
        [".cjs"] = "javascript",
        [".jsx"] = "javascriptreact",
    };

    // Returns null for an unmapped extension - callers treat that as "no language server involved
    // for this file," not an error (specs/multi-language-support/requirements.md R2.3).
    public static string? Resolve(string filePath)
    {
        var extension = Path.GetExtension(filePath);

        return !string.IsNullOrEmpty(extension) && ExtensionToLanguageId.TryGetValue(extension, out var languageId)
            ? languageId
            : null;
    }

    // One configured "typescript" LanguageServerOptions entry/process (typescript-language-server)
    // handles four distinct per-file LSP languageIds - typescript, typescriptreact, javascript,
    // javascriptreact - since the server itself distinguishes them (e.g. for JSX parsing) even
    // though they share one running process. This maps a file to the *settings/process* key
    // (LanguageServerOptions.LanguageId) that owns it, which is not always the same string as the
    // per-file languageId Resolve() returns for didOpen.
    private static readonly Dictionary<string, string> LanguageIdToServerGroup = new(StringComparer.Ordinal)
    {
        ["csharp"] = "csharp",
        ["python"] = "python",
        ["typescript"] = "typescript",
        ["typescriptreact"] = "typescript",
        ["javascript"] = "typescript",
        ["javascriptreact"] = "typescript",
    };

    public static string? ResolveServerGroup(string filePath) =>
        Resolve(filePath) is { } languageId && LanguageIdToServerGroup.TryGetValue(languageId, out var group) ? group : null;
}
