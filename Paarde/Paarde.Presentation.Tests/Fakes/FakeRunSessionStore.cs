using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeRunSessionStore : IRunSessionStore
{
    public int RecordRunCallCount { get; private set; }

    public string? LastRecordedPrompt { get; private set; }

    public string? LastRecordedRawOutput { get; private set; }

    public string? LastRecordedUserPromptText { get; private set; }

    public int ResaveCallCount { get; private set; }

    public int RestoreLastSessionCallCount { get; private set; }

    public int ListSessionsCallCount { get; private set; }

    // Null by default (no prior run to restore) - tests that need a restorable session set this.
    public RunSession? SessionToRestore { get; set; }

    // Empty by default - tests that need a listing set this.
    public IReadOnlyList<RunSession> SessionsToList { get; set; } = Array.Empty<RunSession>();

    public void RecordRun(string prompt, string rawOutput, string userPromptText)
    {
        RecordRunCallCount++;
        LastRecordedPrompt = prompt;
        LastRecordedRawOutput = rawOutput;
        LastRecordedUserPromptText = userPromptText;
    }

    public void Resave() => ResaveCallCount++;

    public RunSession? RestoreLastSession()
    {
        RestoreLastSessionCallCount++;
        return SessionToRestore;
    }

    public IReadOnlyList<RunSession> ListSessions()
    {
        ListSessionsCallCount++;
        return SessionsToList;
    }
}
