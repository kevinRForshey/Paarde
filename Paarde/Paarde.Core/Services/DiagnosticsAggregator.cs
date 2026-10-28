using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Core.Services;

// specs/ide-workbench-shell, R4: see IDiagnosticsAggregator. Registered as a DI singleton and
// constructed eagerly alongside WorkbenchDockViewModel (specs/ide-workbench-shell/design.md), the
// same "construct it early so it's already listening" rationale RunPanelViewModel's own eager
// construction already relies on for Build/Run.
public sealed class DiagnosticsAggregator : IDiagnosticsAggregator
{
    private readonly Dictionary<string, IReadOnlyList<LspDiagnostic>> _diagnosticsByFilePath = new(StringComparer.Ordinal);

    public DiagnosticsAggregator(ILanguageServerSessionManager sessionManager) =>
        sessionManager.DiagnosticsPublished += OnDiagnosticsPublished;

    public event Action? Changed;

    public IReadOnlyDictionary<string, IReadOnlyList<LspDiagnostic>> Diagnostics => _diagnosticsByFilePath;

    private void OnDiagnosticsPublished(string filePath, IReadOnlyList<LspDiagnostic> diagnostics)
    {
        // R4.3: a file reporting zero diagnostics is removed outright rather than kept as an empty
        // entry - decision in design.md: this is the *only* way an entry disappears (closing a
        // file in Explorer does not clear it), matching every mainstream IDE's Problems-panel
        // convention that a diagnostic is a property of the file/project, not of whether an editor
        // happens to be open for it.
        if (diagnostics.Count == 0)
        {
            _diagnosticsByFilePath.Remove(filePath);
        }
        else
        {
            _diagnosticsByFilePath[filePath] = diagnostics;
        }

        Changed?.Invoke();
    }
}
