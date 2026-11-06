namespace Paarde.Core.Models;

// Result of parsing `headroom doctor --json` (IHeadroomService.CheckHealthAsync). ExitCode is
// null when the CLI produced no parseable output at all (not installed, crashed, malformed
// JSON) - treated the same as a failing deployment (exit code >= 2) by callers, matching the
// fallback headroom-check.sh/.ps1 already use.
public record HeadroomHealthResult(int? ExitCode, IReadOnlyList<HeadroomCheckItem> Checks)
{
    public bool IsHealthy => ExitCode == 0;

    public bool IsDown => ExitCode is null || ExitCode >= 2;
}

public record HeadroomCheckItem(string Name, string Status, string Summary);
