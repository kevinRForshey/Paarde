using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Tests.Fakes;

public class FakeLanguageServerSessionManager : ILanguageServerSessionManager
{
    public IReadOnlyList<LspLocation> DefinitionsToReturn { get; set; } = Array.Empty<LspLocation>();

    public IReadOnlyList<LspLocation> ReferencesToReturn { get; set; } = Array.Empty<LspLocation>();

    public IReadOnlyList<LspCompletionItem> CompletionsToReturn { get; set; } = Array.Empty<LspCompletionItem>();

    public string? LastStartedWorkingDirectoryPath { get; private set; }

    public bool StopAllCalled { get; private set; }

    public event Action<string, IReadOnlyList<LspDiagnostic>>? DiagnosticsPublished;

    public IReadOnlyCollection<string> RunningLanguageIds { get; set; } = Array.Empty<string>();

    public event Action? RunningLanguageIdsChanged;

    public void RaiseRunningLanguageIdsChanged() => RunningLanguageIdsChanged?.Invoke();

    public Task StartAllAsync(string workingDirectoryPath, CancellationToken cancellationToken = default)
    {
        LastStartedWorkingDirectoryPath = workingDirectoryPath;
        return Task.CompletedTask;
    }

    public Task StopAllAsync()
    {
        StopAllCalled = true;
        return Task.CompletedTask;
    }

    public Task NotifyDocumentOpenedAsync(string filePath, string text) => Task.CompletedTask;

    public Task NotifyDocumentChangedAsync(string filePath, string text) => Task.CompletedTask;

    public Task NotifyDocumentClosedAsync(string filePath) => Task.CompletedTask;

    public Task<IReadOnlyList<LspLocation>> GetDefinitionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult(DefinitionsToReturn);

    public Task<IReadOnlyList<LspLocation>> GetReferencesAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReferencesToReturn);

    public Task<IReadOnlyList<LspCompletionItem>> GetCompletionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult(CompletionsToReturn);

    public void RaiseDiagnosticsPublished(string filePath, IReadOnlyList<LspDiagnostic> diagnostics) =>
        DiagnosticsPublished?.Invoke(filePath, diagnostics);
}
