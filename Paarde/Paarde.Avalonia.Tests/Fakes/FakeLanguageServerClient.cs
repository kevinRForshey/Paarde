using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Tests.Fakes;

public class FakeLanguageServerClient : ILanguageServerClient
{
    public bool IsRunning { get; private set; }

    public bool AvailableToReturn { get; set; } = true;

    public IReadOnlyList<LspLocation> DefinitionsToReturn { get; set; } = Array.Empty<LspLocation>();

    public IReadOnlyList<LspLocation> ReferencesToReturn { get; set; } = Array.Empty<LspLocation>();

    public IReadOnlyList<LspCompletionItem> CompletionsToReturn { get; set; } = Array.Empty<LspCompletionItem>();

    public string? LastStartedWorkingDirectoryPath { get; private set; }

    public event Action<string, IReadOnlyList<LspDiagnostic>>? DiagnosticsPublished;

    public Task<bool> IsAvailableAsync() => Task.FromResult(AvailableToReturn);

    public Task StartAsync(string workingDirectoryPath, CancellationToken cancellationToken = default)
    {
        IsRunning = true;
        LastStartedWorkingDirectoryPath = workingDirectoryPath;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsRunning = false;
        return Task.CompletedTask;
    }

    public string? LastOpenedLanguageId { get; private set; }

    public Task NotifyDocumentOpenedAsync(string filePath, string text, string languageId)
    {
        LastOpenedLanguageId = languageId;
        return Task.CompletedTask;
    }

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
