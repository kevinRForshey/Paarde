using Paarde.Core.Models;

namespace Paarde.Avalonia.ViewModels;

// specs/ide-workbench-shell R1.4: a pure presentation flattening of IDiagnosticsAggregator's
// per-file diagnostics into rows the Problems tab's ListBox binds to - built in
// ShellView.axaml.cs's code-behind, not a new ViewModel-level collection type (design.md).
public record ProblemRow(string FilePath, LspPosition Position, LspDiagnosticSeverity Severity, string Message)
{
    public string Summary => $"{FilePath}:{Position.Line + 1}:{Position.Character + 1} [{Severity}] {Message}";
}
