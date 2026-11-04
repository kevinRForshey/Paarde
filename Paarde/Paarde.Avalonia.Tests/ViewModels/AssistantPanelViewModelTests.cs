using Paarde.Avalonia.Models;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.Tests.Fakes;
using Paarde.Avalonia.ViewModels;
using Paarde.Core.Services;

namespace Paarde.Avalonia.Tests.ViewModels;

// Epic 25, T2. Uses the real PromptResultStore (rather than FakePromptResultStore, which
// intentionally raises PropertyChanged with a null PropertyName for bulk-invalidation
// subscribers) so SetResult's Tutorial change raises a property-named notification, the same as
// production - that's exactly what AssistantPanelViewModel's constructor subscribes to.
public class AssistantPanelViewModelTests
{
    private readonly PromptResultStore _promptResultStore = new();
    private readonly FakeLlmRunner _llmRunner = new();
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakeFolderPickerService _folderPickerService = new();
    private readonly FakeFilePickerService _filePickerService = new();
    private readonly PromptTemplateProvider _promptTemplateProvider = new();
    private readonly FakeRunSessionStore _runSessionStore = new();
    private readonly FakeCodebaseIndexService _codebaseIndexService = new();
    private readonly FakeWorkingDirectoryContext _workingDirectoryContext = new();
    private readonly FakeApplyOrchestrationService _applyOrchestrationService = new();
    private readonly FakeApplyHistoryLog _applyHistoryLog = new();
    private readonly FakeDialogService _dialogService = new();
    private readonly FakeFileSavePickerService _fileSavePickerService = new();
    private readonly FakeBuildTestRunner _buildTestRunner = new();
    private readonly FakeNavigationService _navigationService = new();

    private AssistantPanelViewModel CreateSut()
    {
        var compose = new PromptViewModel(_llmRunner, _localSettingsService, _folderPickerService, _filePickerService, _promptResultStore, _promptTemplateProvider, _runSessionStore, _codebaseIndexService, _workingDirectoryContext);
        var review = new TutorialViewModel(_promptResultStore, _applyOrchestrationService, _applyHistoryLog, _dialogService, _llmRunner, _promptTemplateProvider, _fileSavePickerService, _localSettingsService, _buildTestRunner);

        return new AssistantPanelViewModel(compose, review, _promptResultStore, _localSettingsService, _navigationService);
    }

    [Fact]
    public void ShowCompose_SetsSelectedTabIndexToZero()
    {
        var sut = CreateSut();
        sut.ShowReview();

        sut.ShowCompose();

        Assert.Equal(0, sut.SelectedTabIndex);
    }

    [Fact]
    public void ShowReview_SetsSelectedTabIndexToOne()
    {
        var sut = CreateSut();

        sut.ShowReview();

        Assert.Equal(1, sut.SelectedTabIndex);
    }

    [Fact]
    public void Constructor_ComposeAndReviewExposeTheGivenInstances()
    {
        var compose = new PromptViewModel(_llmRunner, _localSettingsService, _folderPickerService, _filePickerService, _promptResultStore, _promptTemplateProvider, _runSessionStore, _codebaseIndexService, _workingDirectoryContext);
        var review = new TutorialViewModel(_promptResultStore, _applyOrchestrationService, _applyHistoryLog, _dialogService, _llmRunner, _promptTemplateProvider, _fileSavePickerService, _localSettingsService, _buildTestRunner);

        var sut = new AssistantPanelViewModel(compose, review, _promptResultStore, _localSettingsService, _navigationService);

        Assert.Same(compose, sut.Compose);
        Assert.Same(review, sut.Review);
    }

    [Fact]
    public void PromptResultStore_TutorialChanges_AutomaticallyShowsReview()
    {
        var sut = CreateSut();
        Assert.Equal(0, sut.SelectedTabIndex);

        _promptResultStore.SetResult("A finished run's narrative.", [], "/tmp");

        Assert.Equal(1, sut.SelectedTabIndex);
    }

    [Fact]
    public void PromptResultStore_WorkingDirectoryChangesWithoutTutorialChanging_DoesNotSwitchTabs()
    {
        var sut = CreateSut();
        _promptResultStore.SetResult(string.Empty, [], "/tmp/one");
        sut.ShowCompose();

        _promptResultStore.WorkingDirectoryPath = "/tmp/two";

        Assert.Equal(0, sut.SelectedTabIndex);
    }

    [Fact]
    public async Task PromptResultStore_TutorialChanges_EditorFocusedMode_DoesNotNavigate()
    {
        var sut = CreateSut();

        _promptResultStore.SetResult("A finished run's narrative.", [], "/tmp");
        await Task.Yield();

        Assert.Equal(0, _navigationService.NavigateToCallCount);
    }

    [Fact]
    public async Task PromptResultStore_TutorialChanges_FlowWizardMode_NavigatesToReview()
    {
        await _localSettingsService.SaveSettingAsync(InterfaceModeSettingsKeys.Mode, InterfaceMode.FlowWizard);
        var sut = CreateSut();

        _promptResultStore.SetResult("A finished run's narrative.", [], "/tmp");
        await Task.Yield();

        Assert.Equal(1, _navigationService.NavigateToCallCount);
        Assert.Equal(typeof(TutorialViewModel).FullName, _navigationService.LastPageKey);
    }
}
