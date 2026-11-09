using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeLlmRunner : ILlmRunner
{
    public LlmRunResult ResultToReturn { get; set; } = new() { Success = true, Output = string.Empty };

    // T5.1.2: when set, each successive call to RunPromptAsync dequeues the next result instead of
    // always returning ResultToReturn - lets retry tests give the first (schema-invalid) and second
    // (corrected) call different outputs. Falls back to ResultToReturn once exhausted.
    public Queue<LlmRunResult>? ResultsToReturnInOrder { get; set; }

    public Exception? ExceptionToThrow { get; set; }

    public string? LastPrompt { get; private set; }

    public IReadOnlyList<string> AllPrompts => _allPrompts;

    private readonly List<string> _allPrompts = [];

    public string? LastWorkingDirectory { get; private set; }

    public int CallCount { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public string? LastResumeSessionId { get; private set; }

    // When false, RunPromptAsync blocks until cancellationToken is cancelled instead of returning
    // immediately - stands in for a real `claude` invocation that's still running, so tests can
    // exercise PromptViewModel.CancelPromptCommand without a real process.
    public bool CompleteImmediately { get; set; } = true;

    public bool AvailableToReturn { get; set; } = true;

    // T6.2.1: text chunks RunPromptAsync should hand to onTextDelta, in order, before returning.
    public IReadOnlyList<string> TextDeltasToEmit { get; set; } = Array.Empty<string>();

    // Epic 11 (F11.3.3): descriptions RunPromptAsync should hand to onToolActivity, in order,
    // before returning - same shape as TextDeltasToEmit above.
    public IReadOnlyList<string> ToolActivitiesToEmit { get; set; } = Array.Empty<string>();

    public Task<bool> IsAvailableAsync() => Task.FromResult(AvailableToReturn);

    public async Task<LlmRunResult> RunPromptAsync(string prompt, string? workingDirectory = null, Action<string>? onTextDelta = null, Action<string>? onToolActivity = null, string? resumeSessionId = null, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastPrompt = prompt;
        _allPrompts.Add(prompt);
        LastWorkingDirectory = workingDirectory;
        LastResumeSessionId = resumeSessionId;
        LastCancellationToken = cancellationToken;

        if (ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }

        if (!CompleteImmediately)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        foreach (var delta in TextDeltasToEmit)
        {
            onTextDelta?.Invoke(delta);
        }

        foreach (var activity in ToolActivitiesToEmit)
        {
            onToolActivity?.Invoke(activity);
        }

        return ResultsToReturnInOrder is { Count: > 0 } queue ? queue.Dequeue() : ResultToReturn;
    }
}
