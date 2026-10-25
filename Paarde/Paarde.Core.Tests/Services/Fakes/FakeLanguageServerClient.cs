using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Core.Tests.Services.Fakes;

public class FakeLanguageServerClient : ILanguageServerClient
{
    public bool IsRunning { get; private set; }

    public bool AvailableToReturn { get; set; } = true;

    public bool ThrowOnStart { get; set; }

    public string? LastStartedWorkingDirectoryPath { get; private set; }

    public string? LastOpenedFilePath { get; private set; }

    public string? LastOpenedLanguageId { get; private set; }

    public bool StopCalled { get; private set; }

    public event Action<string, IReadOnlyList<LspDiagnostic>>? DiagnosticsPublished;

    public Task<bool> IsAvailableAsync() => Task.FromResult(AvailableToReturn);

    public Task StartAsync(string workingDirectoryPath, CancellationToken cancellationToken = default)
    {
        if (ThrowOnStart)
        {
            throw new InvalidOperationException("Simulated start failure.");
        }

        IsRunning = true;
        LastStartedWorkingDirectoryPath = workingDirectoryPath;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopCalled = true;
        IsRunning = false;
        return Task.CompletedTask;
    }

    public Task NotifyDocumentOpenedAsync(string filePath, string text, string languageId)
    {
        LastOpenedFilePath = filePath;
        LastOpenedLanguageId = languageId;
        return Task.CompletedTask;
    }

    public Task NotifyDocumentChangedAsync(string filePath, string text) => Task.CompletedTask;

    public Task NotifyDocumentClosedAsync(string filePath) => Task.CompletedTask;

    public Task<IReadOnlyList<LspLocation>> GetDefinitionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LspLocation>>([]);

    public Task<IReadOnlyList<LspLocation>> GetReferencesAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LspLocation>>([]);

    public Task<IReadOnlyList<LspCompletionItem>> GetCompletionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LspCompletionItem>>([]);

    public void RaiseDiagnosticsPublished(string filePath, IReadOnlyList<LspDiagnostic> diagnostics) =>
        DiagnosticsPublished?.Invoke(filePath, diagnostics);
}
