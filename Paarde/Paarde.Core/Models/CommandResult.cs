namespace Paarde.Core.Models;

// Result of one IBuildTestRunner.RunAsync call - deliberately the same shape
// ClaudeCliRunner/GitStatusService already capture from a child process (exit code, stdout,
// stderr), just without any CLI-specific fields (cost, tokens, session id) that don't apply to an
// arbitrary shell command.
public record CommandResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration)
{
    public bool Succeeded => ExitCode == 0;
}
