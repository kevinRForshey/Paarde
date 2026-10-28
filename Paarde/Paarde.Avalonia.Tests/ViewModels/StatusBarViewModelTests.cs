using Paarde.Avalonia.Services;
using Paarde.Avalonia.Tests.Fakes;
using Paarde.Avalonia.ViewModels;
using Paarde.Core.Models;
using Paarde.Core.ViewModels;

namespace Paarde.Avalonia.Tests.ViewModels;

public class StatusBarViewModelTests
{
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakeWorkingDirectoryContext _workingDirectoryContext = new();
    private readonly FakeGitStatusService _gitStatusService = new();
    private readonly FakeLanguageServerSessionManager _languageServerSessionManager = new();
    private readonly FakeActiveEditorContext _activeEditorContext = new();

    private readonly FakeProjectTypeDetector _projectTypeDetector = new();
    private readonly FakeRunConfigurationDetector _runConfigurationDetector = new();
    private readonly FakeBuildTestRunner _buildTestRunner = new();
    private readonly FakePtyProcessManager _ptyProcessManager = new();
    private readonly FakeWorkingDirectoryContext _runPanelWorkingDirectoryContext = new();
    private readonly RunPanelViewModel _runPanelViewModel;

    public StatusBarViewModelTests()
    {
        var runConfigurationsProvider = new RunConfigurationsProvider(_localSettingsService, _projectTypeDetector, _runConfigurationDetector);
        _runPanelViewModel = new RunPanelViewModel(runConfigurationsProvider, _buildTestRunner, _ptyProcessManager, _localSettingsService, _runPanelWorkingDirectoryContext);
    }

    private StatusBarViewModel CreateSut() =>
        new(_workingDirectoryContext, _gitStatusService, _languageServerSessionManager, _runPanelViewModel, _activeEditorContext, _localSettingsService);

    private static RunConfiguration MakeConfiguration(string name = "My App") =>
        new("config-1", name, "csharp", "/repo/src/MyApp", "src/MyApp/MyApp.csproj", "dotnet build", "dotnet run", IsAutoDetected: true);

    // RefreshGitStatusAsync's Task.Run hops onto the thread pool, so a fixed number of Task.Yield()
    // calls on the test's own thread is a race, not a guarantee - poll instead.
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Construction_NoSavedWorkingDirectory_LeavesBranchAndDirtyCountAtDefaults()
    {
        var sut = CreateSut();
        await Task.Yield();

        Assert.Null(sut.GitBranchName);
        Assert.Equal(0, sut.DirtyFileCount);
    }

    // Construction's LoadSavedWorkingDirectoryAsync (below) calls SetWorkingDirectoryAsync directly,
    // not through Dispatcher.UIThread.Post - unlike OnWorkingDirectoryChanged itself (the live-
    // update path fired by IWorkingDirectoryContext.Changed), which RunPanelViewModelTests'
    // equivalent tests already document as unexercised here for the same reason (nothing pumps
    // Dispatcher.UIThread in this plain xunit host).
    [Fact]
    public async Task Construction_SavedWorkingDirectoryIsARepositoryWithDirtyFiles_UpdatesBranchAndDirtyCount()
    {
        await _localSettingsService.SaveSettingAsync(WorkingDirectorySettingsKeys.WorkingDirectory, "/repo");
        _gitStatusService.StatusToReturn = new GitStatus(IsRepository: true, BranchName: "feature-branch", DirtyFiles: ["a.cs", "b.cs"]);

        var sut = CreateSut();
        await WaitUntilAsync(() => sut.GitBranchName is not null);

        Assert.Equal("feature-branch", sut.GitBranchName);
        Assert.Equal(2, sut.DirtyFileCount);
    }

    [Fact]
    public void Construction_DefaultRunConfigurationName_IsNone()
    {
        var sut = CreateSut();

        Assert.Equal("(none)", sut.RunConfigurationName);
    }

    [Fact]
    public void RunPanelSelectedConfigurationChanged_UpdatesRunConfigurationName()
    {
        var sut = CreateSut();

        _runPanelViewModel.SelectedConfiguration = MakeConfiguration("My App");

        Assert.Equal("My App", sut.RunConfigurationName);
    }

    [Fact]
    public void RunPanelSelectedConfigurationChanged_ToNull_RevertsToNoneLabel()
    {
        var sut = CreateSut();
        _runPanelViewModel.SelectedConfiguration = MakeConfiguration();

        _runPanelViewModel.SelectedConfiguration = null;

        Assert.Equal("(none)", sut.RunConfigurationName);
    }

    [Fact]
    public void Construction_DefaultLanguageServerStatus_IsNone()
    {
        var sut = CreateSut();

        Assert.Equal("LSP: none", sut.LanguageServerStatus);
    }

    // RunningLanguageIdsChanged/DiagnosticsPublished both marshal through Dispatcher.UIThread.Post
    // (the former defensively, the latter because it's genuinely raised from a background LSP-read
    // thread - see StatusBarViewModel's own remarks), so, same as the git-status case above, this
    // exercises RefreshLanguageServerStatus() - the synchronous logic those handlers call - directly
    // rather than through the events themselves.
    [Fact]
    public void RefreshLanguageServerStatus_SomeLanguagesRunning_JoinsThemIntoLanguageServerStatus()
    {
        var sut = CreateSut();
        _languageServerSessionManager.RunningLanguageIds = ["csharp", "python"];

        sut.RefreshLanguageServerStatus();

        Assert.Equal("LSP: csharp, python", sut.LanguageServerStatus);
    }

    [Fact]
    public void RefreshLanguageServerStatus_NoLanguagesRunning_ReportsNone()
    {
        var sut = CreateSut();
        _languageServerSessionManager.RunningLanguageIds = Array.Empty<string>();

        sut.RefreshLanguageServerStatus();

        Assert.Equal("LSP: none", sut.LanguageServerStatus);
    }

    [Fact]
    public void Construction_CaretPositionText_IsNull()
    {
        var sut = CreateSut();

        Assert.Null(sut.CaretPositionText);
    }

    [Fact]
    public async Task ActiveEditorContextChanged_WithPosition_FormatsAsOneBasedLineAndColumn()
    {
        var sut = CreateSut();

        _activeEditorContext.SetCaretPosition(new LspPosition(Line: 4, Character: 9));
        await Task.Yield();

        Assert.Equal("Ln 5, Col 10", sut.CaretPositionText);
    }

    [Fact]
    public async Task ActiveEditorContextChanged_BackToNull_ClearsCaretPositionText()
    {
        var sut = CreateSut();
        _activeEditorContext.SetCaretPosition(new LspPosition(0, 0));
        await Task.Yield();

        _activeEditorContext.SetCaretPosition(null);
        await Task.Yield();

        Assert.Null(sut.CaretPositionText);
    }
}
