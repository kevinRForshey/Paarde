namespace Paarde.Core.Models;

public enum LspDiagnosticSeverity
{
    Error,
    Warning,
    Information,
    Hint,
}

public record LspDiagnostic(LspPosition Start, LspPosition End, string Message, LspDiagnosticSeverity Severity);
