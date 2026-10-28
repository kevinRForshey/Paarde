using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Core.Tests.Services.Fakes;

public class FakeLanguageServerSessionManager : ILanguageServerSessionManager
{
    public IReadOnlyCollection<string> RunningLanguageIds { get; set; } = Array.Empty<string>();

    public event Action<string, IReadOnlyList<LspDiagnostic>>? DiagnosticsPublished;

    public event Action? RunningLanguageIdsChanged;

    public void RaiseRunningLanguageIdsChanged() => RunningLanguageIdsChanged?.Invoke();

    public Task StartAllAsync(string workingDirectoryPath, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAllAsync() => Task.CompletedTask;

    public Task NotifyDocumentOpenedAsync(string filePath, string text) => Task.CompletedTask;

    public Task NotifyDocumentChangedAsync(string filePath, string text) => Task.CompletedTask;

    public Task NotifyDocumentClosedAsync(string filePath) => Task.CompletedTask;

    public Task<IReadOnlyList<LspLocation>> GetDefinitionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LspLocation>>(Array.Empty<LspLocation>());

    public Task<IReadOnlyList<LspLocation>> GetReferencesAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LspLocation>>(Array.Empty<LspLocation>());

    public Task<IReadOnlyList<LspCompletionItem>> GetCompletionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LspCompletionItem>>(Array.Empty<LspCompletionItem>());

    public void RaiseDiagnosticsPublished(string filePath, IReadOnlyList<LspDiagnostic> diagnostics) =>
        DiagnosticsPublished?.Invoke(filePath, diagnostics);
}
