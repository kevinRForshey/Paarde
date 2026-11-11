using Paarde.Core.Models;

namespace Paarde.Presentation.ViewModels;

// A pure presentation flattening of IDiagnosticsAggregator's
// per-file diagnostics into rows the Problems tab's ListBox binds to - built in
// ShellView.axaml.cs's code-behind, not a new ViewModel-level collection type (design.md).
public record ProblemRow(string FilePath, LspPosition Position, LspDiagnosticSeverity Severity, string Message)
{
    // File name only: a full path pushed the message itself off the edge of the dock. The full path
    // is the row's tooltip.
    public string Summary => $"{Path.GetFileName(FilePath)}:{Position.Line + 1}:{Position.Character + 1} [{Severity}] {Message}";
}
