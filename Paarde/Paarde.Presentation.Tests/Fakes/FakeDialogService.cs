using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeDialogService : IDialogService
{
    public bool ConfirmResultToReturn { get; set; } = true;

    // Takes precedence over ConfirmResultToReturn when non-empty (one dequeue per ConfirmAsync
    // call), so a test can give different answers to successive confirm dialogs - e.g. the
    // general apply-confirm and the T4.1.2 protected-paths confirm - without every other existing
    // test (which only sets ConfirmResultToReturn and expects it to apply to every call) having to
    // change.
    public Queue<bool> ConfirmResultQueue { get; } = new();

    public List<string> ConfirmTitles { get; } = new();

    public List<string> ConfirmMessages { get; } = new();

    public string? LastConfirmTitle { get; private set; }

    public string? LastConfirmMessage { get; private set; }

    public string? LastInfoTitle { get; private set; }

    public string? LastInfoMessage { get; private set; }

    public int ConfirmCallCount { get; private set; }

    public int InfoCallCount { get; private set; }

    public Task<bool> ConfirmAsync(string title, string message, string confirmButtonText, string cancelButtonText)
    {
        ConfirmCallCount++;
        LastConfirmTitle = title;
        LastConfirmMessage = message;
        ConfirmTitles.Add(title);
        ConfirmMessages.Add(message);

        var result = ConfirmResultQueue.Count > 0 ? ConfirmResultQueue.Dequeue() : ConfirmResultToReturn;
        return Task.FromResult(result);
    }

    public Task InfoAsync(string title, string message, string closeButtonText)
    {
        InfoCallCount++;
        LastInfoTitle = title;
        LastInfoMessage = message;
        return Task.CompletedTask;
    }
}
