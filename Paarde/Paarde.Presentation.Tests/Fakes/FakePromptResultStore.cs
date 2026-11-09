using System.Collections.ObjectModel;
using System.ComponentModel;

using Paarde.Core.Contracts.Services;
using Paarde.Core.ViewModels;

namespace Paarde.Presentation.Tests.Fakes;

public class FakePromptResultStore : IPromptResultStore
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Tutorial { get; private set; } = string.Empty;

    public string WorkingDirectoryPath { get; private set; } = string.Empty;

    public string? SessionId { get; private set; }

    public ObservableCollection<ProposedFileChangeViewModel> Files { get; } = new();

    public int SetResultCallCount { get; private set; }

    // T6.2.2: lets a test observe intermediate SetResult calls made while a prompt is still streaming,
    // not just the final one.
    public Action? OnSetResult { get; set; }

    public void SetResult(string tutorial, IReadOnlyList<ProposedFileChangeViewModel> files, string workingDirectoryPath, string? sessionId = null)
    {
        SetResultCallCount++;
        Tutorial = tutorial;
        WorkingDirectoryPath = workingDirectoryPath;
        SessionId = sessionId ?? SessionId;

        Files.Clear();
        foreach (var file in files)
        {
            Files.Add(file);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        OnSetResult?.Invoke();
    }
}
