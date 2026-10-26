using Paarde.Core.Models;
using Paarde.Core.Services;
using Paarde.Core.Tests.Services.Fakes;

namespace Paarde.Core.Tests.Services;

public class LanguageServerSessionManagerTests
{
    private readonly FakeProjectTypeDetector _projectTypeDetector = new();
    private readonly Dictionary<string, FakeLanguageServerClient> _clientsByLanguageId = new(StringComparer.Ordinal);

    private LanguageServerSessionManager CreateSut(IReadOnlyList<LanguageServerOptions> options)
    {
        return new LanguageServerSessionManager(
            _projectTypeDetector,
            () => Task.FromResult(options),
            clientFactory: languageOptions =>
            {
                var client = new FakeLanguageServerClient();
                _clientsByLanguageId[languageOptions.LanguageId] = client;
                return client;
            });
    }

    [Fact]
    public async Task StartAllAsync_LanguageNotDetectedAndNotForceEnabled_NeverStartsThatClient()
    {
        _projectTypeDetector.LanguageRootsToReturn = new Dictionary<string, string> { ["python"] = "/repo" };
        var sut = CreateSut([new LanguageServerOptions("csharp", "csharp-ls", string.Empty)]);

        await sut.StartAllAsync("/repo");

        Assert.Empty(_clientsByLanguageId);
    }

    [Fact]
    public async Task StartAllAsync_LanguageDetected_StartsThatClient()
    {
        _projectTypeDetector.LanguageRootsToReturn = new Dictionary<string, string> { ["python"] = "/repo" };
        var sut = CreateSut([new LanguageServerOptions("python", "pyright-langserver", "--stdio")]);

        await sut.StartAllAsync("/repo");

        Assert.True(_clientsByLanguageId["python"].IsRunning);
        Assert.Equal("/repo", _clientsByLanguageId["python"].LastStartedWorkingDirectoryPath);
    }

    [Fact]
    public async Task StartAllAsync_PolyglotRepoWithDifferentDetectedRoots_StartsEachClientAtItsOwnRoot()
    {
        _projectTypeDetector.LanguageRootsToReturn = new Dictionary<string, string>
        {
            ["python"] = "/repo/backend",
            ["typescript"] = "/repo/frontend",
        };
        var sut = CreateSut([
            new LanguageServerOptions("python", "pyright-langserver", "--stdio"),
            new LanguageServerOptions("typescript", "typescript-language-server", "--stdio"),
        ]);

        await sut.StartAllAsync("/repo");

        Assert.Equal("/repo/backend", _clientsByLanguageId["python"].LastStartedWorkingDirectoryPath);
        Assert.Equal("/repo/frontend", _clientsByLanguageId["typescript"].LastStartedWorkingDirectoryPath);
    }

    [Fact]
    public async Task StartAllAsync_ForceEnabledButNotDetected_StillStartsThatClient()
    {
        _projectTypeDetector.LanguageRootsToReturn = new Dictionary<string, string>();
        var sut = CreateSut([new LanguageServerOptions("csharp", "csharp-ls", string.Empty, ForceEnabled: true)]);

        await sut.StartAllAsync("/repo");

        Assert.True(_clientsByLanguageId["csharp"].IsRunning);
        Assert.Equal("/repo", _clientsByLanguageId["csharp"].LastStartedWorkingDirectoryPath);
    }

    [Fact]
    public async Task StartAllAsync_ExecutableUnavailable_SkipsThatClientSilently()
    {
        _projectTypeDetector.LanguageRootsToReturn = new Dictionary<string, string> { ["csharp"] = "/repo" };

        var sut = new LanguageServerSessionManager(
            _projectTypeDetector,
            () => Task.FromResult<IReadOnlyList<LanguageServerOptions>>([new LanguageServerOptions("csharp", "csharp-ls", string.Empty)]),
            clientFactory: languageOptions =>
            {
                var client = new FakeLanguageServerClient { AvailableToReturn = false };
                _clientsByLanguageId[languageOptions.LanguageId] = client;
                return client;
            });

        await sut.StartAllAsync("/repo");

        await sut.NotifyDocumentOpenedAsync("/repo/a.cs", "class A {}");

        Assert.False(_clientsByLanguageId["csharp"].IsRunning);
        Assert.Null(_clientsByLanguageId["csharp"].LastOpenedFilePath);
    }

    [Fact]
    public async Task StartAllAsync_OneLanguageFailsToStart_OtherLanguageStartsAnyway()
    {
        _projectTypeDetector.LanguageRootsToReturn = new Dictionary<string, string> { ["csharp"] = "/repo", ["python"] = "/repo" };

        var sut = new LanguageServerSessionManager(
            _projectTypeDetector,
            () => Task.FromResult<IReadOnlyList<LanguageServerOptions>>([
                new LanguageServerOptions("csharp", "csharp-ls", string.Empty),
                new LanguageServerOptions("python", "pyright-langserver", "--stdio"),
            ]),
            clientFactory: languageOptions =>
            {
                var client = new FakeLanguageServerClient { ThrowOnStart = languageOptions.LanguageId == "csharp" };
                _clientsByLanguageId[languageOptions.LanguageId] = client;
                return client;
            });

        await sut.StartAllAsync("/repo");

        Assert.False(_clientsByLanguageId["csharp"].IsRunning);
        Assert.True(_clientsByLanguageId["python"].IsRunning);
    }

    [Fact]
    public async Task StartAllAsync_CalledAgainWithSameWorkingDirectory_DoesNotRestartRunningClients()
    {
        _projectTypeDetector.LanguageRootsToReturn = new Dictionary<string, string> { ["csharp"] = "/repo" };
        var sut = CreateSut([new LanguageServerOptions("csharp", "csharp-ls", string.Empty)]);

        await sut.StartAllAsync("/repo");
        var firstClient = _clientsByLanguageId["csharp"];

        await sut.StartAllAsync("/repo");

        Assert.False(firstClient.StopCalled);
    }

    [Fact]
    public async Task StartAllAsync_CalledAgainWithDifferentWorkingDirectory_StopsThePreviousClients()
    {
        _projectTypeDetector.LanguageRootsToReturn = new Dictionary<string, string> { ["csharp"] = "/repo-one" };
        var sut = CreateSut([new LanguageServerOptions("csharp", "csharp-ls", string.Empty)]);

        await sut.StartAllAsync("/repo-one");
        var firstClient = _clientsByLanguageId["csharp"];

        await sut.StartAllAsync("/repo-two");

        Assert.True(firstClient.StopCalled);
    }

    [Fact]
    public async Task StopAllAsync_StopsEveryStartedClient()
    {
        _projectTypeDetector.LanguageRootsToReturn = new Dictionary<string, string> { ["csharp"] = "/repo", ["python"] = "/repo" };
        var sut = CreateSut([
            new LanguageServerOptions("csharp", "csharp-ls", string.Empty),
            new LanguageServerOptions("python", "pyright-langserver", "--stdio"),
        ]);
        await sut.StartAllAsync("/repo");

        await sut.StopAllAsync();

        Assert.True(_clientsByLanguageId["csharp"].StopCalled);
        Assert.True(_clientsByLanguageId["python"].StopCalled);
    }

    [Fact]
    public async Task NotifyDocumentOpenedAsync_MappedExtensionWithRunningClient_RoutesToThatClientWithResolvedLanguageId()
    {
        _projectTypeDetector.LanguageRootsToReturn = new Dictionary<string, string> { ["typescript"] = "/repo" };
        var sut = CreateSut([new LanguageServerOptions("typescript", "typescript-language-server", "--stdio")]);
        await sut.StartAllAsync("/repo");

        await sut.NotifyDocumentOpenedAsync("/repo/App.tsx", "const x = 1;");

        Assert.Equal("/repo/App.tsx", _clientsByLanguageId["typescript"].LastOpenedFilePath);
        Assert.Equal("typescriptreact", _clientsByLanguageId["typescript"].LastOpenedLanguageId);
    }

    [Fact]
    public async Task NotifyDocumentOpenedAsync_UnmappedExtension_IsANoOp()
    {
        _projectTypeDetector.LanguageRootsToReturn = new Dictionary<string, string> { ["csharp"] = "/repo" };
        var sut = CreateSut([new LanguageServerOptions("csharp", "csharp-ls", string.Empty)]);
        await sut.StartAllAsync("/repo");

        await sut.NotifyDocumentOpenedAsync("/repo/README.md", "# Hello");

        Assert.Null(_clientsByLanguageId["csharp"].LastOpenedFilePath);
    }

    [Fact]
    public async Task NotifyDocumentOpenedAsync_MappedExtensionButNoClientRunningForIt_IsANoOp()
    {
        _projectTypeDetector.LanguageRootsToReturn = new Dictionary<string, string>();
        var sut = CreateSut([new LanguageServerOptions("python", "pyright-langserver", "--stdio")]);
        await sut.StartAllAsync("/repo");

        await sut.NotifyDocumentOpenedAsync("/repo/main.py", "x = 1");

        Assert.DoesNotContain("python", _clientsByLanguageId.Keys);
    }
}
