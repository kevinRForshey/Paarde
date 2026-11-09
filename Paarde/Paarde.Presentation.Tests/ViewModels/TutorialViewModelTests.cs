using Paarde.Presentation.Tests.Fakes;
using Paarde.Core.Models;
using Paarde.Core.Services;
using Paarde.Core.ViewModels;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;

namespace Paarde.Presentation.Tests.ViewModels;

public class TutorialViewModelTests
{
    private readonly FakePromptResultStore _promptResultStore = new();
    private readonly FakeApplyOrchestrationService _applyOrchestrationService = new();
    private readonly FakeApplyHistoryLog _applyHistoryLog = new();
    private readonly FakeDialogService _dialogService = new();
    private readonly FakeLlmRunner _llmRunner = new();
    private readonly PromptTemplateProvider _promptTemplateProvider = new();
    private readonly FakeFileSavePickerService _fileSavePickerService = new();
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakeBuildTestRunner _buildTestRunner = new();

    private TutorialViewModel CreateSut() => new(_promptResultStore, _applyOrchestrationService, _applyHistoryLog, _dialogService, _llmRunner, _promptTemplateProvider, _fileSavePickerService, _localSettingsService, _buildTestRunner);

    // Epic 25, T5: ExplorerViewModel now takes a real AssistantPanelViewModel (wrapping its own,
    // unrelated PromptViewModel/TutorialViewModel pair, each needing only fakes for their own
    // dependencies) rather than a fake - these helpers don't exercise that wiring at all, so a
    // throwaway panel/store built fresh each time is simplest.
    private static ExplorerViewModel CreateStandaloneExplorer()
    {
        var promptResultStore = new FakePromptResultStore();
        var localSettingsService = new FakeLocalSettingsService();
        var llmRunner = new FakeLlmRunner();
        var compose = new PromptViewModel(llmRunner, localSettingsService, new FakeFolderPickerService(), new FakeFilePickerService(), promptResultStore, new PromptTemplateProvider(), new FakeRunSessionStore(), new FakeCodebaseIndexService(), new FakeWorkingDirectoryContext(), new FakeUiDispatcher());
        var review = new TutorialViewModel(promptResultStore, new FakeApplyOrchestrationService(), new FakeApplyHistoryLog(), new FakeDialogService(), llmRunner, new PromptTemplateProvider(), new FakeFileSavePickerService(), localSettingsService, new FakeBuildTestRunner());
        var assistantPanel = new AssistantPanelViewModel(compose, review, promptResultStore, localSettingsService, new FakeNavigationService());

        return new ExplorerViewModel(localSettingsService, promptResultStore, llmRunner, new FakeFileChangeApplyService(), new FakeDialogService(), new FakeLanguageServerSessionManager(), new FakeFolderPickerService(), assistantPanel, new FakeUiDispatcher());
    }

    // Epic 25, T4: Selected now reads ExplorerViewModel.SelectedTab?.PendingChange rather than its
    // own settable property - a real ExplorerViewModel (rather than a fake) stands in for it, with
    // SelectedTab assigned directly instead of going through OpenOrActivateTab/a real file tree, so
    // these tests don't need to touch disk.
    private static void Select(TutorialViewModel sut, ProposedFileChangeViewModel? file)
    {
        var explorer = CreateStandaloneExplorer();
        sut.AttachExplorer(explorer);

        if (file is not null)
        {
            explorer.SelectedTab = new OpenFileTabViewModel(file.FilePath, string.Empty, "/tmp", new FakeFileChangeApplyService(), new FakeLlmRunner(), new FakeLanguageServerSessionManager())
            {
                PendingChange = file,
            };
        }
    }

    [Fact]
    public void Constructor_TutorialOverviewAlreadyInTheStore_PopulatesTutorialAndHasTutorialOverview()
    {
        _promptResultStore.SetResult("This run adds a new widget.", [], "/tmp");

        var sut = CreateSut();

        Assert.Equal("This run adds a new widget.", sut.Tutorial);
        Assert.True(sut.HasTutorialOverview);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NoTutorialOverviewInTheStore_HasTutorialOverviewIsFalse(string tutorial)
    {
        _promptResultStore.SetResult(tutorial, [], "/tmp");

        var sut = CreateSut();

        Assert.False(sut.HasTutorialOverview);
    }

    [Fact]
    public void HasNoFiles_NoPromptRunYet_IsTrue()
    {
        var sut = CreateSut();

        Assert.True(sut.HasNoFiles);
        Assert.False(sut.HasFilesButNoneSelected);
    }

    [Fact]
    public void HasFilesButNoneSelected_FilesPresentButNoExplorerSelection_IsTrue()
    {
        _promptResultStore.SetResult("tutorial", [new ProposedFileChangeViewModel("a.txt", "proposed", "original")], "/tmp");

        var sut = CreateSut();

        // Epic 25: unlike the old auto-select-the-first-file behavior, there's no selection at all
        // until Explorer (via AttachExplorer) says otherwise - HasFilesButNoneSelected is true for
        // every file-selecting view model that hasn't attached/selected yet, not just a brief moment.
        Assert.False(sut.HasNoFiles);
        Assert.True(sut.HasFilesButNoneSelected);
    }

    [Fact]
    public void HasFilesButNoneSelected_ExplorerSelectsAFile_BecomesFalse()
    {
        var file = new ProposedFileChangeViewModel("a.txt", "proposed", "original");
        _promptResultStore.SetResult("tutorial", [file], "/tmp");
        var sut = CreateSut();

        Select(sut, file);

        Assert.False(sut.HasFilesButNoneSelected);
        Assert.Same(file, sut.Selected);
    }

    [Fact]
    public void HasFilesButNoneSelected_ExplorerSelectionCleared_IsTrueAgain()
    {
        var file = new ProposedFileChangeViewModel("a.txt", "proposed", "original");
        _promptResultStore.SetResult("tutorial", [file], "/tmp");
        var sut = CreateSut();
        var explorer = CreateStandaloneExplorer();
        sut.AttachExplorer(explorer);
        explorer.SelectedTab = new OpenFileTabViewModel(file.FilePath, string.Empty, "/tmp", new FakeFileChangeApplyService(), new FakeLlmRunner(), new FakeLanguageServerSessionManager()) { PendingChange = file };

        explorer.SelectedTab = null;

        Assert.True(sut.HasFilesButNoneSelected);
        Assert.Null(sut.Selected);
    }

    [Fact]
    public async Task ApplySelectedAsync_NothingSelected_SkipsConfirmationAndReportsNoneSelected()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(0, 0);
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(0, _dialogService.ConfirmCallCount);
        Assert.Equal(0, _applyOrchestrationService.ApplySelectedCallCount);
        Assert.False(string.IsNullOrEmpty(sut.ApplyStatusMessage));
    }

    [Fact]
    public async Task ApplySelectedAsync_UserCancelsConfirmation_DoesNotApply()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = false;
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogService.ConfirmCallCount);
        Assert.Equal(0, _applyOrchestrationService.ApplySelectedCallCount);
    }

    [Fact]
    public async Task ApplySelectedAsync_UserConfirms_AppliesAndReportsResultViaInfoDialog()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(2, 3);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]);
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, _applyOrchestrationService.ApplySelectedCallCount);
        Assert.Equal(1, _dialogService.InfoCallCount);
        Assert.Contains("1", sut.ApplyStatusMessage);
    }

    [Fact]
    public async Task ApplySelectedAsync_FilesStagedInGit_MessageMentionsStaging()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]) { FilesStagedInGit = true };
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Contains("staged".ToLowerInvariant(), sut.ApplyStatusMessage.ToLowerInvariant());
    }

    [Fact]
    public async Task ApplySelectedAsync_FilesNotStagedInGit_MessageDoesNotMentionStaging()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]) { FilesStagedInGit = false };
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.DoesNotContain("staged".ToLowerInvariant(), sut.ApplyStatusMessage.ToLowerInvariant());
    }

    [Fact]
    public async Task ApplySelectedAsync_StageAppliedFilesInGitDefaultsToTrue_PassesTrueThrough()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.True(_applyOrchestrationService.LastStageAppliedFiles);
    }

    [Fact]
    public async Task ApplySelectedAsync_StageAppliedFilesInGitUnchecked_PassesFalseThrough()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        var sut = CreateSut();
        sut.StageAppliedFilesInGit = false;

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.False(_applyOrchestrationService.LastStageAppliedFiles);
    }

    [Fact]
    public async Task ApplySelectedAsync_NoBuildOrTestCommandConfigured_DoesNotRunEither()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]);
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(0, _buildTestRunner.RunAsyncCallCount);
    }

    [Fact]
    public async Task ApplySelectedAsync_NothingActuallyApplied_DoesNotRunBuildOrTestEvenIfConfigured()
    {
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.BuildCommand, "dotnet build");
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Failed)]);
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(0, _buildTestRunner.RunAsyncCallCount);
    }

    [Fact]
    public async Task ApplySelectedAsync_BuildCommandConfigured_RunsItInTheWorkingDirectory()
    {
        _promptResultStore.SetResult("tutorial", [], "/tmp/project");
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.BuildCommand, "dotnet build");
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]);
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(0, "build ok", string.Empty, TimeSpan.FromSeconds(1)));
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, _buildTestRunner.RunAsyncCallCount);
        Assert.Equal("dotnet build", _buildTestRunner.CommandsRun[0]);
        Assert.Contains("succeeded", sut.ApplyStatusMessage.ToLowerInvariant());
    }

    [Fact]
    public async Task ApplySelectedAsync_BuildCommandFails_MessageReportsFailureAndSkipsTest()
    {
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.BuildCommand, "dotnet build");
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.TestCommand, "dotnet test");
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]);
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(1, string.Empty, "it broke", TimeSpan.FromSeconds(1)));
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, _buildTestRunner.RunAsyncCallCount);
        Assert.Contains("it broke", sut.ApplyStatusMessage);
        Assert.Contains("skipped", sut.ApplyStatusMessage.ToLowerInvariant());
    }

    [Fact]
    public async Task ApplySelectedAsync_BlankBuildCommandButTestCommandConfigured_SkipsBuildAndRunsTest()
    {
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.BuildCommand, string.Empty);
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.TestCommand, "dotnet test");
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]);
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(0, "tests ok", string.Empty, TimeSpan.FromSeconds(2)));
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, _buildTestRunner.RunAsyncCallCount);
        Assert.Equal("dotnet test", _buildTestRunner.CommandsRun[0]);
        Assert.Contains("passed", sut.ApplyStatusMessage.ToLowerInvariant());
    }

    [Fact]
    public async Task ApplySelectedAsync_TestCommandFails_MessageReportsFailure()
    {
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.BuildCommand, "dotnet build");
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.TestCommand, "dotnet test");
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]);
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(0, "build ok", string.Empty, TimeSpan.FromSeconds(1)));
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(1, string.Empty, "test failure detail", TimeSpan.FromSeconds(3)));
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(2, _buildTestRunner.RunAsyncCallCount);
        Assert.Contains("test failure detail", sut.ApplyStatusMessage);
    }

    [Fact]
    public async Task ApplySelectedAsync_BuildCommandTimesOut_ReportsTimeoutAndSkipsTest()
    {
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.BuildCommand, "dotnet build");
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.TestCommand, "dotnet test");
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]);
        _buildTestRunner.ThrowOperationCanceledOnNextRun = true;
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, _buildTestRunner.RunAsyncCallCount);
        Assert.Contains("timed out", sut.ApplyStatusMessage.ToLowerInvariant());
        Assert.Contains("skipped", sut.ApplyStatusMessage.ToLowerInvariant());
    }

    [Fact]
    public async Task ApplySelectedAsync_BuildFails_SendErrorsToClaudeCommandBecomesAvailable()
    {
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.BuildCommand, "dotnet build");
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]);
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(1, string.Empty, "it broke", TimeSpan.FromSeconds(1)));
        var sut = CreateSut();

        Assert.False(sut.HasPendingBuildTestFailure);

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.True(sut.HasPendingBuildTestFailure);
        Assert.True(sut.SendBuildTestErrorsToClaudeCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApplySelectedAsync_BuildSucceeds_SendErrorsToClaudeCommandStaysUnavailable()
    {
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.BuildCommand, "dotnet build");
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]);
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(0, "ok", string.Empty, TimeSpan.FromSeconds(1)));
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.False(sut.HasPendingBuildTestFailure);
        Assert.False(sut.SendBuildTestErrorsToClaudeCommand.CanExecute(null));
    }

    [Fact]
    public async Task SendBuildTestErrorsToClaudeAsync_Success_ReplacesTheFailingFileAndClearsThePendingFailure()
    {
        _promptResultStore.SetResult("tutorial", [new ProposedFileChangeViewModel("a.txt", "proposed", "original")], "/tmp", "session-1");
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.BuildCommand, "dotnet build");
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]);
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(1, string.Empty, "it broke", TimeSpan.FromSeconds(1)));
        var sut = CreateSut();
        await sut.ApplySelectedCommand.ExecuteAsync(null);

        _llmRunner.ResultToReturn = new LlmRunResult
        {
            Success = true,
            Output = """{"responseType": "development", "tutorial": "Fixed.", "files": [{"path": "a.txt", "content": "fixed content", "steps": []}]}""",
        };

        await sut.SendBuildTestErrorsToClaudeCommand.ExecuteAsync(null);

        Assert.Single(sut.Files);
        Assert.Equal("fixed content", sut.Files[0].ProposedContent);
        Assert.Equal("fixed content", _promptResultStore.Files[0].ProposedContent);
        Assert.Contains("it broke", _llmRunner.LastPrompt);
        Assert.Equal("session-1", _llmRunner.LastResumeSessionId);
        Assert.False(sut.HasPendingBuildTestFailure);
        Assert.Contains("1", sut.SendBuildTestErrorsStatusMessage);
    }

    [Fact]
    public async Task SendBuildTestErrorsToClaudeAsync_ResponseHasNewFile_AddsItToFilesAndTheStore()
    {
        _promptResultStore.SetResult("tutorial", [new ProposedFileChangeViewModel("a.txt", "proposed", "original")], "/tmp", "session-1");
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.BuildCommand, "dotnet build");
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]);
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(1, string.Empty, "it broke", TimeSpan.FromSeconds(1)));
        var sut = CreateSut();
        await sut.ApplySelectedCommand.ExecuteAsync(null);

        _llmRunner.ResultToReturn = new LlmRunResult
        {
            Success = true,
            Output = """{"responseType": "development", "tutorial": "Fixed.", "files": [{"path": "b.txt", "content": "new file content", "steps": []}]}""",
        };

        await sut.SendBuildTestErrorsToClaudeCommand.ExecuteAsync(null);

        Assert.Equal(2, sut.Files.Count);
        Assert.Equal(2, _promptResultStore.Files.Count);
        Assert.Contains(sut.Files, f => f.FilePath == "b.txt" && f.ProposedContent == "new file content");
    }

    [Fact]
    public async Task SendBuildTestErrorsToClaudeAsync_RunFails_ReportsTheErrorAndKeepsThePendingFailure()
    {
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.BuildCommand, "dotnet build");
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]);
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(1, string.Empty, "it broke", TimeSpan.FromSeconds(1)));
        var sut = CreateSut();
        await sut.ApplySelectedCommand.ExecuteAsync(null);

        _llmRunner.ResultToReturn = new LlmRunResult { Success = false, ErrorMessage = "boom" };

        await sut.SendBuildTestErrorsToClaudeCommand.ExecuteAsync(null);

        Assert.True(sut.HasPendingBuildTestFailure);
        Assert.Contains("boom", sut.SendBuildTestErrorsStatusMessage);
    }

    [Fact]
    public async Task Constructor_SavedStageAppliedFilesInGitPreferenceIsFalse_RestoresIt()
    {
        await _localSettingsService.SaveSettingAsync("Tutorial_StageAppliedFilesInGit", false);

        var sut = CreateSut();
        await Task.Yield();

        Assert.False(sut.StageAppliedFilesInGit);
    }

    [Fact]
    public async Task OnStageAppliedFilesInGitChanged_SavesThePreference()
    {
        var sut = CreateSut();

        sut.StageAppliedFilesInGit = false;

        Assert.False(await _localSettingsService.ReadSettingAsync<bool?>("Tutorial_StageAppliedFilesInGit"));
    }

    [Fact]
    public async Task ApplySelectedAsync_SomeFilesConflictOrFail_MessageMentionsEachAffectedFile()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(2, 2);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
        [
            new FileApplyResult("conflicted.txt", FileApplyOutcome.Conflict),
            new FileApplyResult("failed.txt", FileApplyOutcome.Failed, "disk full"),
        ]);
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Contains("conflicted.txt", sut.ApplyStatusMessage);
        Assert.Contains("failed.txt", sut.ApplyStatusMessage);
    }

    [Fact]
    public async Task ApplySelectedAsync_NoProtectedFiles_OnlyShowsOneConfirmationDialog()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1) { ProtectedPathCount = 0 };
        _dialogService.ConfirmResultToReturn = true;
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogService.ConfirmCallCount);
        Assert.True(_applyOrchestrationService.LastAllowProtectedPaths);
    }

    [Fact]
    public async Task ApplySelectedAsync_ProtectedFilesAndUserConfirmsBoth_AppliesWithProtectedPathsAllowed()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1) { ProtectedPathCount = 1, ProtectedPathFiles = ["bin/output.txt"] };
        _dialogService.ConfirmResultQueue.Enqueue(true);
        _dialogService.ConfirmResultQueue.Enqueue(true);
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(2, _dialogService.ConfirmCallCount);
        Assert.True(_applyOrchestrationService.LastAllowProtectedPaths);
        Assert.Contains("bin/output.txt", _dialogService.ConfirmMessages[1]);
    }

    [Fact]
    public async Task ApplySelectedAsync_ProtectedFilesAndUserDeclinesProtectedConfirm_StillAppliesWithoutAllowingProtectedPaths()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1) { ProtectedPathCount = 1, ProtectedPathFiles = ["bin/output.txt"] };
        _dialogService.ConfirmResultQueue.Enqueue(true);
        _dialogService.ConfirmResultQueue.Enqueue(false);
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, _applyOrchestrationService.ApplySelectedCallCount);
        Assert.False(_applyOrchestrationService.LastAllowProtectedPaths);
    }

    [Fact]
    public async Task ApplySelectedAsync_UserDeclinesGeneralConfirm_NeverAsksAboutProtectedPaths()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1) { ProtectedPathCount = 1, ProtectedPathFiles = ["bin/output.txt"] };
        _dialogService.ConfirmResultQueue.Enqueue(false);
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogService.ConfirmCallCount);
        Assert.Equal(0, _applyOrchestrationService.ApplySelectedCallCount);
    }

    [Fact]
    public async Task ApplySelectedAsync_NoGitDirtyFiles_OnlyShowsOneConfirmationDialog()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1) { IsGitRepository = true, GitDirtyFileCount = 0 };
        _dialogService.ConfirmResultToReturn = true;
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogService.ConfirmCallCount);
        Assert.Equal(1, _applyOrchestrationService.ApplySelectedCallCount);
    }

    [Fact]
    public async Task ApplySelectedAsync_GitDirtyFilesAndUserConfirmsBoth_Applies()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1) { IsGitRepository = true, GitDirtyFileCount = 1, GitDirtyFiles = ["a.txt"] };
        _dialogService.ConfirmResultQueue.Enqueue(true);
        _dialogService.ConfirmResultQueue.Enqueue(true);
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(2, _dialogService.ConfirmCallCount);
        Assert.Equal(1, _applyOrchestrationService.ApplySelectedCallCount);
        Assert.Contains("a.txt", _dialogService.ConfirmMessages[1]);
    }

    [Fact]
    public async Task ApplySelectedAsync_GitDirtyFilesAndUserDeclinesDirtyConfirm_CancelsTheWholeApply()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1) { IsGitRepository = true, GitDirtyFileCount = 1, GitDirtyFiles = ["a.txt"] };
        _dialogService.ConfirmResultQueue.Enqueue(true);
        _dialogService.ConfirmResultQueue.Enqueue(false);
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(2, _dialogService.ConfirmCallCount);
        Assert.Equal(0, _applyOrchestrationService.ApplySelectedCallCount);
    }

    [Fact]
    public async Task ApplySelectedAsync_UserDeclinesGeneralConfirm_NeverAsksAboutGitDirtyFiles()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1) { IsGitRepository = true, GitDirtyFileCount = 1, GitDirtyFiles = ["a.txt"] };
        _dialogService.ConfirmResultQueue.Enqueue(false);
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogService.ConfirmCallCount);
        Assert.Equal(0, _applyOrchestrationService.ApplySelectedCallCount);
    }

    [Fact]
    public async Task ApplySelectedAsync_SomeFilesProtectedPathBlocked_MessageMentionsEachBlockedFile()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("bin/output.txt", FileApplyOutcome.ProtectedPathBlocked)]);
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.Contains("bin/output.txt", sut.ApplyStatusMessage);
    }

    [Fact]
    public async Task UndoLastApplyAsync_UserCancelsConfirmation_DoesNotUndo()
    {
        _dialogService.ConfirmResultToReturn = false;
        var sut = CreateSut();

        await sut.UndoLastApplyCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogService.ConfirmCallCount);
        Assert.Equal(0, _applyOrchestrationService.TryUndoLastApplyCallCount);
    }

    [Fact]
    public async Task UndoLastApplyAsync_NothingToUndo_ReportsItWithoutShowingResultDialog()
    {
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.TryUndoLastApplyResultToReturn = false;
        var sut = CreateSut();

        await sut.UndoLastApplyCommand.ExecuteAsync(null);

        Assert.Equal(0, _dialogService.InfoCallCount);
        Assert.False(string.IsNullOrEmpty(sut.ApplyStatusMessage));
    }

    [Fact]
    public async Task UndoLastApplyAsync_UserConfirms_UndoesAndReportsResultViaInfoDialog()
    {
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.TryUndoLastApplyResultToReturn = true;
        _applyOrchestrationService.UndoLastApplyRevertResultToReturn = new RevertResult([new RevertFileResult("a.txt", true)]);
        var sut = CreateSut();

        await sut.UndoLastApplyCommand.ExecuteAsync(null);

        Assert.Equal(1, _applyOrchestrationService.TryUndoLastApplyCallCount);
        Assert.Equal(1, _dialogService.InfoCallCount);
        Assert.Contains("1", sut.ApplyStatusMessage);
    }

    [Fact]
    public async Task UndoLastApplyAsync_RestoredFileIsInFilesCollection_RefreshesItsOriginalContent()
    {
        _promptResultStore.SetResult("tutorial", [new ProposedFileChangeViewModel("a.txt", "proposed", "stale baseline")], Path.GetTempPath());
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.TryUndoLastApplyResultToReturn = true;
        _applyOrchestrationService.UndoLastApplyRevertResultToReturn = new RevertResult([new RevertFileResult("a.txt", true)]);
        var sut = CreateSut();

        await sut.UndoLastApplyCommand.ExecuteAsync(null);

        Assert.NotEqual("stale baseline", sut.Files[0].OriginalContent);
    }

    [Fact]
    public async Task UndoLastApplyAsync_SomeFilesFailedToRestore_MessageMentionsEachFailedFile()
    {
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.TryUndoLastApplyResultToReturn = true;
        _applyOrchestrationService.UndoLastApplyRevertResultToReturn = new RevertResult(
            [new RevertFileResult("failed.txt", false, "disk full")]);
        var sut = CreateSut();

        await sut.UndoLastApplyCommand.ExecuteAsync(null);

        Assert.Contains("failed.txt", sut.ApplyStatusMessage);
    }

    [Fact]
    public async Task RevertAllAppliedAsync_UserCancelsConfirmation_DoesNotRevert()
    {
        _dialogService.ConfirmResultToReturn = false;
        var sut = CreateSut();

        await sut.RevertAllAppliedCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogService.ConfirmCallCount);
        Assert.Equal(0, _applyOrchestrationService.RevertAllAppliedCallCount);
    }

    [Fact]
    public async Task RevertAllAppliedAsync_NothingToRevert_ReportsItWithoutShowingResultDialog()
    {
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.RevertAllAppliedResultToReturn = new RevertResult(Array.Empty<RevertFileResult>());
        var sut = CreateSut();

        await sut.RevertAllAppliedCommand.ExecuteAsync(null);

        Assert.Equal(0, _dialogService.InfoCallCount);
        Assert.False(string.IsNullOrEmpty(sut.ApplyStatusMessage));
    }

    [Fact]
    public async Task RevertAllAppliedAsync_UserConfirms_RevertsAndReportsResultViaInfoDialog()
    {
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.RevertAllAppliedResultToReturn = new RevertResult(
            [new RevertFileResult("a.txt", true), new RevertFileResult("b.txt", true)]);
        var sut = CreateSut();

        await sut.RevertAllAppliedCommand.ExecuteAsync(null);

        Assert.Equal(1, _applyOrchestrationService.RevertAllAppliedCallCount);
        Assert.Equal(1, _dialogService.InfoCallCount);
        Assert.Contains("2", sut.ApplyStatusMessage);
    }

    [Fact]
    public async Task RevertAllAppliedAsync_SomeFilesFailedToRevert_MessageMentionsEachFailedFile()
    {
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.RevertAllAppliedResultToReturn = new RevertResult(
            [new RevertFileResult("failed.txt", false, "disk full")]);
        var sut = CreateSut();

        await sut.RevertAllAppliedCommand.ExecuteAsync(null);

        Assert.Contains("failed.txt", sut.ApplyStatusMessage);
    }

    [Fact]
    public void HasApplyHistory_NoHistoryLogged_IsFalse()
    {
        var sut = CreateSut();

        Assert.False(sut.HasApplyHistory);
        Assert.Empty(sut.ApplyHistoryEntries);
    }

    [Fact]
    public void Constructor_HistoryAlreadyLogged_PopulatesApplyHistoryEntries()
    {
        _applyHistoryLog.Record(ApplyHistoryEntryKind.Applied, new[] { "a.txt" });

        var sut = CreateSut();

        Assert.True(sut.HasApplyHistory);
        Assert.Single(sut.ApplyHistoryEntries);
    }

    [Fact]
    public async Task ApplySelectedAsync_UserConfirms_AddsAnEntryToApplyHistoryEntries()
    {
        _applyOrchestrationService.PreviewToReturn = new ApplyPreview(1, 1);
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.ApplySelectedResultToReturn = new ApplyBatchResult(
            [new FileApplyResult("a.txt", FileApplyOutcome.Applied)]);
        _applyHistoryLog.Record(ApplyHistoryEntryKind.Applied, new[] { "a.txt" });
        var sut = CreateSut();

        await sut.ApplySelectedCommand.ExecuteAsync(null);

        Assert.True(sut.HasApplyHistory);
        Assert.Single(sut.ApplyHistoryEntries);
    }

    [Fact]
    public async Task UndoLastApplyAsync_UserConfirms_RefreshesApplyHistoryEntriesNewestFirst()
    {
        // The real IApplyHistoryLog entry for this undo is recorded by ApplyOrchestrationService
        // itself (not the fake standing in for it here), so seed the log the way it would look right
        // after a real TryUndoLastApply call, then verify the ViewModel's post-action refresh picks it up.
        _applyHistoryLog.Record(ApplyHistoryEntryKind.Applied, new[] { "a.txt" });
        _applyHistoryLog.Record(ApplyHistoryEntryKind.UndoLastApply, new[] { "a.txt" });
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.TryUndoLastApplyResultToReturn = true;
        _applyOrchestrationService.UndoLastApplyRevertResultToReturn = new RevertResult([new RevertFileResult("a.txt", true)]);
        var sut = CreateSut();

        await sut.UndoLastApplyCommand.ExecuteAsync(null);

        Assert.Equal(2, sut.ApplyHistoryEntries.Count);
        Assert.Contains("Undid last apply", sut.ApplyHistoryEntries[0]);
    }

    [Fact]
    public async Task RevertAllAppliedAsync_UserConfirms_RefreshesApplyHistoryEntries()
    {
        // Same reasoning as the undo test above: seed the log entry the real orchestration service
        // would have recorded, then verify the ViewModel's post-action refresh displays it.
        _applyHistoryLog.Record(ApplyHistoryEntryKind.RevertAllApplied, new[] { "a.txt" });
        _dialogService.ConfirmResultToReturn = true;
        _applyOrchestrationService.RevertAllAppliedResultToReturn = new RevertResult(
            [new RevertFileResult("a.txt", true)]);
        var sut = CreateSut();

        await sut.RevertAllAppliedCommand.ExecuteAsync(null);

        Assert.True(sut.HasApplyHistory);
        Assert.Contains("Reverted all applied changes", sut.ApplyHistoryEntries[0]);
    }

    [Fact]
    public void CanRefineSelectedFile_NoFileSelected_CannotBeExecuted()
    {
        var sut = CreateSut();
        sut.RefineInstruction = "make it better";

        Assert.False(sut.RefineSelectedFileCommand.CanExecute(null));
    }

    [Fact]
    public void CanRefineSelectedFile_NoInstructionText_CannotBeExecuted()
    {
        _promptResultStore.SetResult("tutorial", [new ProposedFileChangeViewModel("a.txt", "proposed", "original")], "/tmp");
        var sut = CreateSut();

        Assert.False(sut.RefineSelectedFileCommand.CanExecute(null));
    }

    [Fact]
    public void CanRefineSelectedFile_FileSelectedAndInstructionGiven_CanBeExecuted()
    {
        _promptResultStore.SetResult("tutorial", [new ProposedFileChangeViewModel("a.txt", "proposed", "original")], "/tmp");
        var sut = CreateSut();
        Select(sut, sut.Files[0]);
        sut.RefineInstruction = "make it better";

        Assert.True(sut.RefineSelectedFileCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefineSelectedFileAsync_Success_ReplacesTheFileInBothFilesAndThePromptResultStore()
    {
        _promptResultStore.SetResult("tutorial", [new ProposedFileChangeViewModel("a.txt", "proposed", "original")], "/tmp", "session-1");
        var sut = CreateSut();
        Select(sut, sut.Files[0]);
        sut.RefineInstruction = "add a comment";
        _llmRunner.ResultToReturn = new LlmRunResult
        {
            Success = true,
            Output = """{"responseType": "development", "tutorial": "Refined.", "files": [{"path": "a.txt", "content": "refined content", "steps": []}]}""",
        };

        await sut.RefineSelectedFileCommand.ExecuteAsync(null);

        Assert.Equal("refined content", sut.Files[0].ProposedContent);
        Assert.Equal("refined content", _promptResultStore.Files[0].ProposedContent);
    }

    [Fact]
    public async Task RefineSelectedFileAsync_Success_ResumesTheTrackedSessionIdAndClearsTheInstruction()
    {
        _promptResultStore.SetResult("tutorial", [new ProposedFileChangeViewModel("a.txt", "proposed", "original")], "/tmp", "session-1");
        var sut = CreateSut();
        Select(sut, sut.Files[0]);
        sut.RefineInstruction = "add a comment";
        _llmRunner.ResultToReturn = new LlmRunResult
        {
            Success = true,
            Output = """{"responseType": "development", "tutorial": "Refined.", "files": [{"path": "a.txt", "content": "refined content", "steps": []}]}""",
        };

        await sut.RefineSelectedFileCommand.ExecuteAsync(null);

        Assert.Equal("session-1", _llmRunner.LastResumeSessionId);
        Assert.Equal(string.Empty, sut.RefineInstruction);
    }

    [Fact]
    public async Task RefineSelectedFileAsync_RunFails_ReportsTheErrorWithoutReplacingTheFile()
    {
        _promptResultStore.SetResult("tutorial", [new ProposedFileChangeViewModel("a.txt", "proposed", "original")], "/tmp");
        var sut = CreateSut();
        Select(sut, sut.Files[0]);
        sut.RefineInstruction = "add a comment";
        _llmRunner.ResultToReturn = new LlmRunResult { Success = false, ErrorMessage = "boom" };
        var originalFile = sut.Files[0];

        await sut.RefineSelectedFileCommand.ExecuteAsync(null);

        Assert.Same(originalFile, sut.Files[0]);
        Assert.Contains("a.txt", sut.RefineStatusMessage);
        Assert.Contains("boom", sut.RefineStatusMessage);
    }

    [Fact]
    public async Task RefineSelectedFileAsync_ResponseHasNoMatchingFile_ReportsItWithoutReplacingTheFile()
    {
        _promptResultStore.SetResult("tutorial", [new ProposedFileChangeViewModel("a.txt", "proposed", "original")], "/tmp");
        var sut = CreateSut();
        Select(sut, sut.Files[0]);
        sut.RefineInstruction = "add a comment";
        _llmRunner.ResultToReturn = new LlmRunResult
        {
            Success = true,
            Output = """{"responseType": "development", "tutorial": "Refined.", "files": []}""",
        };
        var originalFile = sut.Files[0];

        await sut.RefineSelectedFileCommand.ExecuteAsync(null);

        Assert.Same(originalFile, sut.Files[0]);
        Assert.Contains("a.txt", sut.RefineStatusMessage);
    }

    [Fact]
    public void CanExportTutorial_NoFilesAndNoOverview_IsFalse()
    {
        var sut = CreateSut();

        Assert.False(sut.ExportTutorialAsMarkdownCommand.CanExecute(null));
        Assert.False(sut.ExportTutorialAsHtmlCommand.CanExecute(null));
    }

    [Fact]
    public void CanExportTutorial_OverviewOnlyNoFiles_IsTrue()
    {
        _promptResultStore.SetResult("This run adds a widget.", [], "/tmp");
        var sut = CreateSut();

        Assert.True(sut.ExportTutorialAsMarkdownCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExportTutorialAsMarkdownAsync_UserCancelsPicker_WritesNothingAndLeavesStatusEmpty()
    {
        _promptResultStore.SetResult("tutorial", [new ProposedFileChangeViewModel("a.txt", "proposed", "original")], "/tmp");
        var sut = CreateSut();
        _fileSavePickerService.PathToReturn = null;

        await sut.ExportTutorialAsMarkdownCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, sut.ExportStatusMessage);
    }

    [Fact]
    public async Task ExportTutorialAsMarkdownAsync_UserPicksAPath_WritesTheMarkdownAndReportsSuccess()
    {
        _promptResultStore.SetResult(
            "Adds a widget.",
            [new ProposedFileChangeViewModel("a.txt", "proposed", "original") { Hunks = [new ProposedHunk("Add it", "Explanation.", "snippet", null)] }],
            "/tmp");
        var sut = CreateSut();
        var path = Path.Combine(Path.GetTempPath(), $"paarde-export-test-{Guid.NewGuid():N}.md");
        _fileSavePickerService.PathToReturn = path;

        try
        {
            await sut.ExportTutorialAsMarkdownCommand.ExecuteAsync(null);

            var written = await File.ReadAllTextAsync(path);
            Assert.Contains("Adds a widget.", written);
            Assert.Contains("### Step 1: Add it", written);
            Assert.Contains(path, sut.ExportStatusMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportTutorialAsHtmlAsync_UserPicksAPath_WritesTheHtml()
    {
        _promptResultStore.SetResult(
            "Adds a widget.",
            [new ProposedFileChangeViewModel("a.txt", "proposed", "original") { Hunks = [new ProposedHunk("Add it", "Explanation.", "snippet", null)] }],
            "/tmp");
        var sut = CreateSut();
        var path = Path.Combine(Path.GetTempPath(), $"paarde-export-test-{Guid.NewGuid():N}.html");
        _fileSavePickerService.PathToReturn = path;

        try
        {
            await sut.ExportTutorialAsHtmlCommand.ExecuteAsync(null);

            var written = await File.ReadAllTextAsync(path);
            Assert.StartsWith("<!DOCTYPE html>", written);
            Assert.Contains("Add it", written);
        }
        finally
        {
            File.Delete(path);
        }
    }

}
