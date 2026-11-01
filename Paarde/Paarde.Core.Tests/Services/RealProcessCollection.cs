namespace Paarde.Core.Tests.Services;

// Every class here spawns a real OS child process - either via System.Diagnostics.Process
// (ClaudeCliRunner/GitStatusService/StdioLanguageServerClient/ProcessCommandRunner) or a raw
// fork() outside the CLR's process table (PtyProcessManager, via Porta.Pty's native shim). Both
// paths ultimately reap the child's exit status through the same OS-level waitpid() mechanism,
// and xUnit runs test classes in parallel by default - so on Linux, the CLR's own SIGCHLD
// handling for one class's Process.Start() can occasionally win a race and reap a PTY test's
// forked child first, permanently losing its exit notification (ProcessExited never fires,
// hanging PtyProcessManagerTests until its timeout). Forcing every class below into one
// serialized collection removes the race entirely.
[CollectionDefinition("RealProcess", DisableParallelization = true)]
public class RealProcessCollection;
