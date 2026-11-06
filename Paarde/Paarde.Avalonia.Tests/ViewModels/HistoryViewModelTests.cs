using Paarde.Avalonia.Tests.Fakes;
using Paarde.Avalonia.ViewModels;
using Paarde.Core.Models;
using Paarde.Core.Services;

namespace Paarde.Avalonia.Tests.ViewModels;

public class HistoryViewModelTests
{
    private readonly FakeRunSessionStore _runSessionStore = new();
    private readonly FakeNavigationService _navigationService = new();
    private readonly FakeLlmRunner _llmRunner = new();
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakePromptResultStore _promptResultStore = new();

    // Epic 25, T7: Fork/Rerun now drive the docked Assistant panel's own Compose view model
    // directly rather than navigating with a PromptLaunchRequest parameter, so a real
    // AssistantPanelViewModel (wrapping its own, otherwise-unrelated PromptViewModel/
    // TutorialViewModel pair) stands in for it here too.
    private AssistantPanelViewModel CreateAssistantPanel()
    {
        var compose = new PromptViewModel(_llmRunner, _localSettingsService, new FakeFolderPickerService(), new FakeFilePickerService(), _promptResultStore, new PromptTemplateProvider(), new FakeRunSessionStore(), new FakeCodebaseIndexService(), new FakeWorkingDirectoryContext());
        var review = new TutorialViewModel(_promptResultStore, new FakeApplyOrchestrationService(), new FakeApplyHistoryLog(), new FakeDialogService(), _llmRunner, new PromptTemplateProvider(), new FakeFileSavePickerService(), _localSettingsService, new FakeBuildTestRunner());

        return new AssistantPanelViewModel(compose, review, _promptResultStore, _localSettingsService, _navigationService);
    }

    private HistoryViewModel CreateSut() => new(_runSessionStore, _navigationService, CreateAssistantPanel());

    private static RunSession CreateSession(string userPromptText, DateTimeOffset startedAt, IReadOnlyList<RunSessionFile>? files = null) => new(
        Guid.NewGuid().ToString("N"),
        startedAt,
        "/working/dir",
        "composed prompt",
        "raw output",
        "tutorial text",
        files ?? Array.Empty<RunSessionFile>(),
        Array.Empty<ApplyHistoryEntry>(),
        userPromptText);

    private static RunSessionFile CreateFile(string filePath) =>
        new(filePath, "original", "proposed", "proposed", true, Array.Empty<RunSessionHunk>());

    [Fact]
    public void OnNavigatedTo_NoSessions_HasNoSessionsAndNothingSelected()
    {
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.True(sut.HasNoSessions);
        Assert.Null(sut.Selected);
    }

    [Fact]
    public void OnNavigatedTo_PopulatesSessionsFromListSessions()
    {
        _runSessionStore.SessionsToList = new[]
        {
            CreateSession("second prompt", DateTimeOffset.UtcNow),
            CreateSession("first prompt", DateTimeOffset.UtcNow.AddMinutes(-1)),
        };
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.Equal(2, sut.Sessions.Count);
        Assert.False(sut.HasNoSessions);
    }

    [Fact]
    public void OnNavigatedTo_AutoSelectsTheFirstSession()
    {
        var session = CreateSession("only prompt", DateTimeOffset.UtcNow);
        _runSessionStore.SessionsToList = new[] { session };
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.NotNull(sut.Selected);
        Assert.Equal(session, sut.Selected!.Session);
    }

    [Fact]
    public void SelectingASession_PopulatesSelectedFilesFromItsFiles()
    {
        var session = CreateSession("prompt", DateTimeOffset.UtcNow, new[] { CreateFile("a.cs"), CreateFile("b.cs") });
        _runSessionStore.SessionsToList = new[] { session };
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.Equal(2, sut.SelectedFiles.Count);
        Assert.Equal("a.cs", sut.SelectedFile!.FilePath);
        Assert.False(sut.SelectedHasNoFiles);
    }

    [Fact]
    public void SelectingASessionWithNoFiles_SelectedHasNoFilesIsTrue()
    {
        _runSessionStore.SessionsToList = new[] { CreateSession("prompt", DateTimeOffset.UtcNow) };
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.Empty(sut.SelectedFiles);
        Assert.True(sut.SelectedHasNoFiles);
    }

    [Fact]
    public void NoSessionSelected_CommandsCannotExecute()
    {
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.False(sut.ForkCommand.CanExecute(null));
        Assert.False(sut.RerunCommand.CanExecute(null));
    }

    [Fact]
    public void ForkCommand_LoadsComposeAndNavigatesToExplorerWithoutAutoRunning()
    {
        _runSessionStore.SessionsToList = new[] { CreateSession("fork this prompt", DateTimeOffset.UtcNow) };
        var assistantPanel = CreateAssistantPanel();
        var sut = new HistoryViewModel(_runSessionStore, _navigationService, assistantPanel);
        sut.OnNavigatedTo(null!);

        sut.ForkCommand.Execute(null);

        Assert.Equal("fork this prompt", assistantPanel.Compose.PromptText);
        Assert.Equal("/working/dir", assistantPanel.Compose.WorkingDirectoryPath);
        Assert.Equal(0, assistantPanel.SelectedTabIndex);
        Assert.Equal(1, _navigationService.NavigateToCallCount);
        Assert.Equal(typeof(ExplorerViewModel).FullName, _navigationService.LastPageKey);
    }

    [Fact]
    public void RerunCommand_LoadsComposeAndNavigatesToExplorerWithAutoRun()
    {
        _runSessionStore.SessionsToList = new[] { CreateSession("rerun this prompt", DateTimeOffset.UtcNow) };
        _llmRunner.ResultToReturn = new LlmRunResult
        {
            Success = true,
            Output = """{"responseType": "answer", "answer": "An answer."}""",
        };
        var assistantPanel = CreateAssistantPanel();
        var sut = new HistoryViewModel(_runSessionStore, _navigationService, assistantPanel);
        sut.OnNavigatedTo(null!);

        sut.RerunCommand.Execute(null);

        Assert.Equal("rerun this prompt", assistantPanel.Compose.PromptText);
        Assert.Equal(typeof(ExplorerViewModel).FullName, _navigationService.LastPageKey);
    }
}
