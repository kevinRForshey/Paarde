using Paarde.Core.Models;

namespace Paarde.Core.Contracts.Services;

// specs/ide-workbench-shell, R4: a session-wide view of every file's latest LSP diagnostics, not
// just whichever one ExplorerViewModel currently has open. Subscribes to
// ILanguageServerSessionManager.DiagnosticsPublished directly (R4.2, R4.4) rather than being fed
// secondhand through a page ViewModel, so it keeps listening for the whole app session regardless
// of which page (if any) is currently active.
public interface IDiagnosticsAggregator
{
    // Raised whenever the aggregated set changes - a file's diagnostics were replaced, or a file
    // that just reported zero diagnostics was removed (R4.3).
    event Action? Changed;

    // Every file path with at least one outstanding diagnostic, as of the most recent publish
    // this instance has seen. A snapshot, not a live view - subscribe to Changed to react to
    // updates rather than polling this.
    IReadOnlyDictionary<string, IReadOnlyList<LspDiagnostic>> Diagnostics { get; }
}
