using Paarde.Core.Models;

namespace Paarde.Core.Contracts.Services;

// Epic 18 (specs/multi-language-support): the new layer between ExplorerViewModel and
// ILanguageServerClient - owns zero-or-more per-language client instances for the current working
// directory and routes every call to whichever one (if any) handles the file involved, by
// extension (LanguageIdResolver). ILanguageServerClient itself is unchanged and still represents
// exactly one running server process for exactly one language; this interface is the multi-
// language-aware façade ExplorerViewModel actually depends on now.
public interface ILanguageServerSessionManager
{
    // Re-raised verbatim from whichever underlying client published it - filePath is still enough
    // for a subscriber to filter to "the file I currently have open," same as Epic 17.
    event Action<string, IReadOnlyList<LspDiagnostic>>? DiagnosticsPublished;

    // Detects which languages are present (IProjectTypeDetector) and starts a server for each one
    // that's both detected (or force-enabled) and available, independently - one language's
    // missing executable or failed start never prevents another's from starting.
    Task StartAllAsync(string workingDirectoryPath, CancellationToken cancellationToken = default);

    // Stops every currently-running server this session started, not just one.
    Task StopAllAsync();

    // Every method below is a documented no-op (notifications) or returns an empty result
    // (requests) if filePath's extension has no mapped languageId, or maps to a language whose
    // server isn't running - callers don't need their own "is this file LSP-backed" check.
    Task NotifyDocumentOpenedAsync(string filePath, string text);

    Task NotifyDocumentChangedAsync(string filePath, string text);

    Task NotifyDocumentClosedAsync(string filePath);

    Task<IReadOnlyList<LspLocation>> GetDefinitionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LspLocation>> GetReferencesAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default);

    // specs/editor-autocomplete: same no-client/unmapped-extension no-op rule as every other
    // request above.
    Task<IReadOnlyList<LspCompletionItem>> GetCompletionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default);
}
