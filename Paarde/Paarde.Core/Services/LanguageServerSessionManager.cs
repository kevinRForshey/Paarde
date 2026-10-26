using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.Models;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Paarde.Core.Services;

// specs/multi-language-support/design.md: owns zero-or-more StdioLanguageServerClient instances,
// one per detected/available language, keyed by LanguageServerOptions.LanguageId ("csharp",
// "python", "typescript" - a settings/process key, not necessarily the same string as a given
// file's own LSP languageId; see LanguageIdResolver). Registered as a DI singleton, same lifetime
// rationale Epic 17 gave the single client it replaces: Explorer only ever has one active working
// directory at a time.
public class LanguageServerSessionManager : ILanguageServerSessionManager
{
    private readonly IProjectTypeDetector _projectTypeDetector;
    private readonly Func<Task<IReadOnlyList<LanguageServerOptions>>> _optionsProvider;
    private readonly Func<LanguageServerOptions, ILanguageServerClient> _clientFactory;
    private readonly ILogger<LanguageServerSessionManager> _logger;

    private readonly Dictionary<string, ILanguageServerClient> _clientsByServerGroup = new(StringComparer.Ordinal);
    private string? _startedWorkingDirectoryPath;

    public LanguageServerSessionManager(
        IProjectTypeDetector projectTypeDetector,
        Func<Task<IReadOnlyList<LanguageServerOptions>>> optionsProvider,
        Func<LanguageServerOptions, ILanguageServerClient>? clientFactory = null,
        ILogger<LanguageServerSessionManager>? logger = null)
    {
        _projectTypeDetector = projectTypeDetector;
        _optionsProvider = optionsProvider;
        _clientFactory = clientFactory ?? (options => new StdioLanguageServerClient(() => Task.FromResult(options)));
        _logger = logger ?? NullLogger<LanguageServerSessionManager>.Instance;
    }

    public event Action<string, IReadOnlyList<LspDiagnostic>>? DiagnosticsPublished;

    // R3.1/R4: evaluates every configured language independently - detected-or-force-enabled and
    // executable-available - so a repo with only some of the configured languages' markers starts
    // only those servers. R3.4: a try/catch per language means one failure never blocks another.
    public async Task StartAllAsync(string workingDirectoryPath, CancellationToken cancellationToken = default)
    {
        // Mirrors Epic 17's single-client "no-op if already running against the same path" guard -
        // ExplorerViewModel calls this on every navigation to the page (it's AddTransient), so
        // without this a revisit would needlessly kill and relaunch every running server.
        if (_clientsByServerGroup.Count > 0 && string.Equals(_startedWorkingDirectoryPath, workingDirectoryPath, StringComparison.Ordinal))
        {
            return;
        }

        await StopAllAsync();
        _startedWorkingDirectoryPath = workingDirectoryPath;

        var detectedLanguageRoots = _projectTypeDetector.DetectLanguageRoots(workingDirectoryPath);
        var allOptions = await _optionsProvider();

        foreach (var options in allOptions)
        {
            var isDetected = detectedLanguageRoots.TryGetValue(options.LanguageId, out var languageRootPath);

            if (!isDetected && !options.ForceEnabled)
            {
                continue;
            }

            try
            {
                var client = _clientFactory(options);

                if (!await client.IsAvailableAsync())
                {
                    continue;
                }

                client.DiagnosticsPublished += OnClientDiagnosticsPublished;

                // Root each server at the directory where its own marker file was found - never at
                // the overall workingDirectoryPath - so typescript-language-server (which resolves
                // its project/toolchain from rootUri at initialize time) finds frontend/'s
                // node_modules/typescript in a polyglot backend/+frontend/ repo instead of failing
                // to resolve a TypeScript installation at all. A force-enabled-but-undetected
                // language has no detected root, so it falls back to workingDirectoryPath.
                await client.StartAsync(isDetected ? languageRootPath! : workingDirectoryPath, cancellationToken);
                _clientsByServerGroup[options.LanguageId] = client;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to start the {LanguageId} language server for {WorkingDirectoryPath}.", options.LanguageId, workingDirectoryPath);
            }
        }
    }

    public async Task StopAllAsync()
    {
        foreach (var client in _clientsByServerGroup.Values)
        {
            client.DiagnosticsPublished -= OnClientDiagnosticsPublished;

            try
            {
                await client.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop a language server cleanly.");
            }
        }

        _clientsByServerGroup.Clear();
    }

    public Task NotifyDocumentOpenedAsync(string filePath, string text)
    {
        var languageId = LanguageIdResolver.Resolve(filePath);

        return languageId is not null && TryGetClient(filePath, out var client)
            ? client.NotifyDocumentOpenedAsync(filePath, text, languageId)
            : Task.CompletedTask;
    }

    public Task NotifyDocumentChangedAsync(string filePath, string text) =>
        TryGetClient(filePath, out var client) ? client.NotifyDocumentChangedAsync(filePath, text) : Task.CompletedTask;

    public Task NotifyDocumentClosedAsync(string filePath) =>
        TryGetClient(filePath, out var client) ? client.NotifyDocumentClosedAsync(filePath) : Task.CompletedTask;

    public Task<IReadOnlyList<LspLocation>> GetDefinitionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        TryGetClient(filePath, out var client) ? client.GetDefinitionAsync(filePath, position, cancellationToken) : Task.FromResult<IReadOnlyList<LspLocation>>([]);

    public Task<IReadOnlyList<LspLocation>> GetReferencesAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        TryGetClient(filePath, out var client) ? client.GetReferencesAsync(filePath, position, cancellationToken) : Task.FromResult<IReadOnlyList<LspLocation>>([]);

    public Task<IReadOnlyList<LspCompletionItem>> GetCompletionAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default) =>
        TryGetClient(filePath, out var client) ? client.GetCompletionAsync(filePath, position, cancellationToken) : Task.FromResult<IReadOnlyList<LspCompletionItem>>([]);

    // R2.2/R2.3: no client registered for the file's resolved server group (unmapped extension, or
    // mapped but that language's server never started) is the documented no-op path, not an error.
    private bool TryGetClient(string filePath, out ILanguageServerClient client)
    {
        var serverGroup = LanguageIdResolver.ResolveServerGroup(filePath);

        if (serverGroup is not null && _clientsByServerGroup.TryGetValue(serverGroup, out var found))
        {
            client = found;
            return true;
        }

        client = null!;
        return false;
    }

    private void OnClientDiagnosticsPublished(string filePath, IReadOnlyList<LspDiagnostic> diagnostics) =>
        DiagnosticsPublished?.Invoke(filePath, diagnostics);
}
