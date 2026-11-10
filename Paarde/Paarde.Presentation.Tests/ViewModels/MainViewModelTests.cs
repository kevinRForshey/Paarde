using Paarde.Presentation.Tests.Fakes;
using Paarde.Core.Models;
using Paarde.Core.Services;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;

namespace Paarde.Presentation.Tests.ViewModels;

// The FlowWizard shell's landing page. Takes a real
// PromptViewModel (rather than a fake) the same way HistoryViewModelTests' AssistantPanelViewModel
// does, since OpenRecentRun/OpenRecentProject/SendQuickPrompt need the genuine
// LoadLaunchRequest/WorkingDirectoryPath wiring, not just a constructor to satisfy.
public class MainViewModelTests
{
    private readonly FakeRunSessionStore _runSessionStore = new();
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakeNavigationService _navigationService = new();
    private readonly FakePromptResultStore _promptResultStore = new();
    private readonly FakeLlmRunner _llmRunner = new();

    private PromptViewModel CreatePromptViewModel() =>
        new(_llmRunner, _localSettingsService, new FakeFolderPickerService(), new FakeFilePickerService(), _promptResultStore, new PromptTemplateProvider(), new FakeRunSessionStore(), new FakeCodebaseIndexService(), new FakeWorkingDirectoryContext(), new FakeUiDispatcher());

    private MainViewModel CreateSut(PromptViewModel? promptViewModel = null) =>
        new(_runSessionStore, _localSettingsService, _navigationService, promptViewModel ?? CreatePromptViewModel());

    private static RunSession CreateSession(string userPromptText, string workingDirectoryPath, DateTimeOffset startedAt) => new(
        Guid.NewGuid().ToString("N"),
        startedAt,
        workingDirectoryPath,
        "composed prompt",
        "raw output",
        "tutorial text",
        Array.Empty<RunSessionFile>(),
        Array.Empty<ApplyHistoryEntry>(),
        userPromptText);

    [Fact]
    public void OnNavigatedTo_NoSessions_HasNeitherRecentRunsNorProjects()
    {
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.False(sut.HasRecentRuns);
        Assert.False(sut.HasRecentProjects);
    }

    [Fact]
    public void OnNavigatedTo_PopulatesRecentRunsFromListSessions()
    {
        _runSessionStore.SessionsToList = new[]
        {
            CreateSession("second prompt", "/working/dir", DateTimeOffset.UtcNow),
            CreateSession("first prompt", "/working/dir", DateTimeOffset.UtcNow.AddMinutes(-1)),
        };
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.Equal(2, sut.RecentRuns.Count);
        Assert.True(sut.HasRecentRuns);
    }

    [Fact]
    public void OnNavigatedTo_PopulatesDistinctRecentProjects()
    {
        _runSessionStore.SessionsToList = new[]
        {
            CreateSession("a", "/working/one", DateTimeOffset.UtcNow),
            CreateSession("b", "/working/one", DateTimeOffset.UtcNow.AddMinutes(-1)),
            CreateSession("c", "/working/two", DateTimeOffset.UtcNow.AddMinutes(-2)),
        };
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.Equal(new[] { "/working/one", "/working/two" }, sut.RecentProjects);
        Assert.True(sut.HasRecentProjects);
    }

    [Fact]
    public void OpenRecentRunCommand_CanExecute_OnlyWhenASessionIsSelected()
    {
        _runSessionStore.SessionsToList = new[] { CreateSession("prompt", "/working/dir", DateTimeOffset.UtcNow) };
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);

        Assert.False(sut.OpenRecentRunCommand.CanExecute(null));

        sut.SelectedRecentRun = sut.RecentRuns[0];

        Assert.True(sut.OpenRecentRunCommand.CanExecute(null));
    }

    [Fact]
    public void OpenRecentRunCommand_LoadsTheRunOntoComposeWithoutAutoRunningAndNavigates()
    {
        _runSessionStore.SessionsToList = new[] { CreateSession("fork this prompt", "/working/dir", DateTimeOffset.UtcNow) };
        var promptViewModel = CreatePromptViewModel();
        var sut = CreateSut(promptViewModel);
        sut.OnNavigatedTo(null!);
        sut.SelectedRecentRun = sut.RecentRuns[0];

        sut.OpenRecentRunCommand.Execute(null);

        Assert.Equal("fork this prompt", promptViewModel.PromptText);
        Assert.Equal("/working/dir", promptViewModel.WorkingDirectoryPath);
        Assert.Equal(1, _navigationService.NavigateToCallCount);
        Assert.Equal(typeof(PromptViewModel).FullName, _navigationService.LastPageKey);
    }

    [Fact]
    public async Task OpenRecentProjectCommand_SavesAndSetsTheWorkingDirectoryThenNavigates()
    {
        _runSessionStore.SessionsToList = new[] { CreateSession("prompt", "/working/dir", DateTimeOffset.UtcNow) };
        var promptViewModel = CreatePromptViewModel();
        var sut = CreateSut(promptViewModel);
        sut.OnNavigatedTo(null!);
        sut.SelectedRecentProject = sut.RecentProjects[0];

        await sut.OpenRecentProjectCommand.ExecuteAsync(null);

        Assert.Equal("/working/dir", promptViewModel.WorkingDirectoryPath);
        Assert.Equal("/working/dir", await _localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory));
        Assert.Equal(typeof(PromptViewModel).FullName, _navigationService.LastPageKey);
    }

    [Fact]
    public void SendQuickPromptCommand_CanExecute_OnlyWhenTextIsNotBlank()
    {
        var sut = CreateSut();

        Assert.False(sut.SendQuickPromptCommand.CanExecute(null));

        sut.QuickPromptText = "Build something.";

        Assert.True(sut.SendQuickPromptCommand.CanExecute(null));
    }

    [Fact]
    public async Task SendQuickPromptCommand_LoadsComposeWithTheSavedWorkingDirectoryAndNavigates()
    {
        await _localSettingsService.SaveSettingAsync(WorkingDirectorySettingsKeys.WorkingDirectory, "/saved/dir");
        var promptViewModel = CreatePromptViewModel();
        var sut = CreateSut(promptViewModel);
        sut.QuickPromptText = "Build something.";

        await sut.SendQuickPromptCommand.ExecuteAsync(null);

        Assert.Equal("Build something.", promptViewModel.PromptText);
        Assert.Equal("/saved/dir", promptViewModel.WorkingDirectoryPath);
        Assert.Equal(typeof(PromptViewModel).FullName, _navigationService.LastPageKey);
    }
}
