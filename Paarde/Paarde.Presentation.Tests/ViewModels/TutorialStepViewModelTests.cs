using Paarde.Presentation.Tests.Fakes;
using Paarde.Core.Models;
using Paarde.Core.Services;
using Paarde.Core.ViewModels;
using Paarde.Presentation.ViewModels;

namespace Paarde.Presentation.Tests.ViewModels;

public class TutorialStepViewModelTests
{
    private readonly FakeFileChangeApplyService _fileChangeApplyService = new();
    private readonly FakeLlmRunner _llmRunner = new();

    // Epic 25, R7: TutorialStepViewModel's "View in Diff" now raises FileFocusRequested on its
    // owning TutorialViewModel instead of navigating - a real one (rather than a fake) since the
    // tests below need its actual FileFocusRequested event, not just a constructor to satisfy.
    private readonly TutorialViewModel _owner = new(
        new FakePromptResultStore(),
        new FakeApplyOrchestrationService(),
        new FakeApplyHistoryLog(),
        new FakeDialogService(),
        new FakeLlmRunner(),
        new PromptTemplateProvider(),
        new FakeFileSavePickerService(),
        new FakeLocalSettingsService(),
        new FakeBuildTestRunner());

    private TutorialStepViewModel CreateSut(string? resumeSessionId = "session-1") =>
        new(
            new ProposedHunk("Step 1", "Explanation", "snippet", null),
            new ProposedFileChangeViewModel("a.cs", "proposed"),
            "/tmp",
            resumeSessionId,
            _fileChangeApplyService,
            _llmRunner,
            _owner,
            stepNumber: 1,
            totalSteps: 1);

    [Fact]
    public void StepLabel_ReflectsTheConstructedStepNumber()
    {
        var sut = new TutorialStepViewModel(
            new ProposedHunk("Step 2", "Explanation", "snippet", null),
            new ProposedFileChangeViewModel("a.cs", "proposed"),
            "/tmp",
            "session-1",
            _fileChangeApplyService,
            _llmRunner,
            _owner,
            stepNumber: 2,
            totalSteps: 4);

        Assert.Equal(2, sut.StepNumber);
        Assert.Equal(4, sut.TotalSteps);
        Assert.Equal("Step 2:", sut.StepLabel);
    }

    [Fact]
    public void CanViewInDiff_HunkWithNoMatchedDiffHunks_IsFalse()
    {
        var sut = CreateSut();

        Assert.False(sut.ViewInDiffCommand.CanExecute(null));
    }

    [Fact]
    public void ViewInDiff_HunkWithAMatchedDiffHunk_RaisesFileFocusRequestedOnTheOwnerWithThatHunk()
    {
        var hunk = new ProposedHunk("Add line2", string.Empty, "line2", new HunkLocation(1, 1));
        var file = new ProposedFileChangeViewModel("a.cs", "line1\nline2", "line1") { Hunks = new[] { hunk } };
        var sut = new TutorialStepViewModel(hunk, file, "/tmp", "session-1", _fileChangeApplyService, _llmRunner, _owner, stepNumber: 1, totalSteps: 1);

        ProposedFileChangeViewModel? focusedFile = null;
        DiffHunk? focusedHunk = null;
        _owner.FileFocusRequested += (f, h) =>
        {
            focusedFile = f;
            focusedHunk = h;
        };

        Assert.True(sut.ViewInDiffCommand.CanExecute(null));
        sut.ViewInDiffCommand.Execute(null);

        Assert.Same(file, focusedFile);
        Assert.Same(hunk.MatchedDiffHunks[0], focusedHunk);
    }

    [Fact]
    public async Task ExplainFurtherAsync_Success_SetsFurtherExplanationFromPlainProseOutput()
    {
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = "Here's a deeper explanation." };
        var sut = CreateSut();

        await sut.ExplainFurtherCommand.ExecuteAsync(null);

        Assert.Equal("Here's a deeper explanation.", sut.FurtherExplanation);
    }

    [Fact]
    public async Task ExplainFurtherAsync_Success_ResumesTheTrackedSessionId()
    {
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = "deeper" };
        var sut = CreateSut(resumeSessionId: "session-42");

        await sut.ExplainFurtherCommand.ExecuteAsync(null);

        Assert.Equal("session-42", _llmRunner.LastResumeSessionId);
    }

    [Fact]
    public async Task ExplainFurtherAsync_OutputIsJsonWrapped_UnwrapsTheProseAnswer()
    {
        _llmRunner.ResultToReturn = new LlmRunResult
        {
            Success = true,
            Output = """{"responseType": "answer", "answer": "The unwrapped answer."}""",
        };
        var sut = CreateSut();

        await sut.ExplainFurtherCommand.ExecuteAsync(null);

        Assert.Equal("The unwrapped answer.", sut.FurtherExplanation);
    }

    [Fact]
    public async Task ExplainFurtherAsync_Failure_SetsFurtherExplanationToAnErrorMessage()
    {
        _llmRunner.ResultToReturn = new LlmRunResult { Success = false, ErrorMessage = "boom" };
        var sut = CreateSut();

        await sut.ExplainFurtherCommand.ExecuteAsync(null);

        Assert.Contains("boom", sut.FurtherExplanation);
    }

    [Fact]
    public void CanExplainFurther_WhileExplaining_CannotBeExecuted()
    {
        var sut = CreateSut();

        sut.IsExplaining = true;

        Assert.False(sut.ExplainFurtherCommand.CanExecute(null));
    }

    [Fact]
    public async Task AskQuestionAsync_Success_SetsQuestionAnswerFromPlainProseOutput()
    {
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = "Here's the answer." };
        var sut = CreateSut();
        sut.QuestionText = "Why does this work?";

        await sut.AskQuestionCommand.ExecuteAsync(null);

        Assert.Equal("Here's the answer.", sut.QuestionAnswer);
    }

    [Fact]
    public async Task AskQuestionAsync_Success_ResumesTheTrackedSessionId()
    {
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = "answer" };
        var sut = CreateSut(resumeSessionId: "session-42");
        sut.QuestionText = "Why?";

        await sut.AskQuestionCommand.ExecuteAsync(null);

        Assert.Equal("session-42", _llmRunner.LastResumeSessionId);
    }

    [Fact]
    public async Task AskQuestionAsync_OutputIsJsonWrapped_UnwrapsTheProseAnswer()
    {
        _llmRunner.ResultToReturn = new LlmRunResult
        {
            Success = true,
            Output = """{"responseType": "answer", "answer": "The unwrapped answer."}""",
        };
        var sut = CreateSut();
        sut.QuestionText = "Why?";

        await sut.AskQuestionCommand.ExecuteAsync(null);

        Assert.Equal("The unwrapped answer.", sut.QuestionAnswer);
    }

    [Fact]
    public async Task AskQuestionAsync_Failure_SetsQuestionAnswerToAnErrorMessage()
    {
        _llmRunner.ResultToReturn = new LlmRunResult { Success = false, ErrorMessage = "boom" };
        var sut = CreateSut();
        sut.QuestionText = "Why?";

        await sut.AskQuestionCommand.ExecuteAsync(null);

        Assert.Contains("boom", sut.QuestionAnswer);
    }

    [Fact]
    public void CanAskQuestion_WithBlankQuestionText_CannotBeExecuted()
    {
        var sut = CreateSut();

        Assert.False(sut.AskQuestionCommand.CanExecute(null));
    }

    [Fact]
    public void CanAskQuestion_WhileAsking_CannotBeExecuted()
    {
        var sut = CreateSut();
        sut.QuestionText = "Why?";

        sut.IsAskingQuestion = true;

        Assert.False(sut.AskQuestionCommand.CanExecute(null));
    }
}
