namespace Paarde.Presentation.Helpers;

// Exit codes that mean the user stopped the command with Ctrl+C, so the Run and Terminal status
// shouldn't report them as a failure.
public static class ExitCodes
{
    // STATUS_CONTROL_C_EXIT: what Windows reports for a console program ended by Ctrl+C.
    private const int WindowsControlCExit = unchecked((int)0xC000013A);

    // 128 + SIGINT: the shell convention for a command ended by Ctrl+C.
    private const int ShellInterruptExit = 130;

    public static bool IsInterrupted(int? exitCode) => exitCode is WindowsControlCExit or ShellInterruptExit;
}
