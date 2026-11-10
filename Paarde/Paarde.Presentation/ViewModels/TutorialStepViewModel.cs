using Paarde.Presentation.Strings;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Paarde.Presentation.ViewModels;

// Wraps a single ProposedHunk with the ability to apply its file's proposed change and report the
// outcome. Applying any step writes the same complete file (one write, selection-aware content via
// ProposedFileChangeViewModel.BuildSelectedContent - see FileChangeApplyService), but "applied"
// itself is per-step: FileChangeApplyService.MarkHunksAppliedFromSelection sets Hunk.IsApplied to
// Hunk.IsSelected right after that write, so a deselected step never reports itself as applied just
// because a sibling step's button was clicked (T3.3.2 - replaces the old MarkAllStepsApplied()
// workaround that marked every step on the file applied unconditionally).
public partial class TutorialStepViewModel : ObservableObject
{
    private readonly ProposedFileChangeViewModel _file;
    private readonly string _workingDirectoryPath;
    private readonly string? _resumeSessionId;
    private readonly IFileChangeApplyService _fileChangeApplyService;
    private readonly ILlmRunner _llmRunner;
    private readonly TutorialViewModel _owner;
    private readonly ILogger<TutorialStepViewModel> _logger;

    public string Title { get; }

    public string Explanation { get; }

    // Exposed so the view can bind its selection/applied state directly to Hunk's properties rather
    // than this class forwarding/duplicating them.
    public ProposedHunk Hunk { get; }

    // This step's 1-based position among its file's steps, and how many there are in total -
    // set by TutorialDetailControl.RebuildSteps from the hunk's index in ProposedFileChangeViewModel.
    // Hunks, which is also the order the steps list (and step-navigation) displays them in.
    public int StepNumber { get; }

    public int TotalSteps { get; }

    public string StepLabel => $"Step {StepNumber}:";

    [ObservableProperty]
    private string statusMessage = string.Empty;

    // The deeper explanation from ExplainFurtherCommand, empty until that's been clicked.
    [ObservableProperty]
    private string furtherExplanation = string.Empty;

    [ObservableProperty]
    private bool isExplaining;

    // A free-form question the user types about this step, as opposed to ExplainFurther's
    // fixed "go deeper" prompt.
    [ObservableProperty]
    private string questionText = string.Empty;

    [ObservableProperty]
    private string questionAnswer = string.Empty;

    [ObservableProperty]
    private bool isAskingQuestion;

    public TutorialStepViewModel(ProposedHunk hunk, ProposedFileChangeViewModel file, string workingDirectoryPath, string? resumeSessionId, IFileChangeApplyService fileChangeApplyService, ILlmRunner llmRunner, TutorialViewModel owner, int stepNumber, int totalSteps, ILogger<TutorialStepViewModel>? logger = null)
    {
        Title = hunk.Title;
        Explanation = hunk.Explanation;
        Hunk = hunk;
        StepNumber = stepNumber;
        TotalSteps = totalSteps;
        _file = file;
        _workingDirectoryPath = workingDirectoryPath;
        _resumeSessionId = resumeSessionId;
        _fileChangeApplyService = fileChangeApplyService;
        _llmRunner = llmRunner;
        _owner = owner;
        _logger = logger ?? NullLogger<TutorialStepViewModel>.Instance;

        // Hunk.IsApplied/MatchedDiffHunks live on a different ObservableObject than this view
        // model's own [ObservableProperty]s, so CanApply/CanViewInDiff need an explicit nudge
        // whenever either changes.
        Hunk.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProposedHunk.IsApplied))
            {
                ApplyCommand.NotifyCanExecuteChanged();
            }
            else if (e.PropertyName == nameof(ProposedHunk.MatchedDiffHunks))
            {
                ViewInDiffCommand.NotifyCanExecuteChanged();
            }
        };
    }

    partial void OnIsExplainingChanged(bool value) => ExplainFurtherCommand.NotifyCanExecuteChanged();

    partial void OnIsAskingQuestionChanged(bool value) => AskQuestionCommand.NotifyCanExecuteChanged();

    partial void OnQuestionTextChanged(string value) => AskQuestionCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        try
        {
            _fileChangeApplyService.Apply(_file, _workingDirectoryPath);
            StatusMessage = string.Format("Tutorial_ChangeApplied".Resolve(), _file.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to apply {FilePath} in {WorkingDirectory}.", _file.FilePath, _workingDirectoryPath);
            StatusMessage = string.Format("Tutorial_ApplyFailed".Resolve(), ex.Message);
        }
    }

    private bool CanApply() => !Hunk.IsApplied && !string.IsNullOrWhiteSpace(_workingDirectoryPath);

    // Asks the owning TutorialViewModel to focus this step's change - replaces the old
    // DiffJumpRequest/NavigateTo(DiffsViewModel) jump now that there's no separate Diffs page to
    // navigate to. Disabled when the step's Location didn't resolve to any DiffHunk (e.g. the
    // snippet turned out identical to what was already there) - there's nothing to focus in that case.
    [RelayCommand(CanExecute = nameof(CanViewInDiff))]
    private void ViewInDiff() => _owner.RequestFileFocus(_file, Hunk.MatchedDiffHunks[0]);

    private bool CanViewInDiff() => Hunk.MatchedDiffHunks.Count > 0;

    // A scoped follow-up asking Claude to go deeper on just this one step, resuming the
    // conversation that produced it rather than starting fresh - so it already has the
    // whole file/tutorial as context and only needs to be told which step to elaborate on.
    [RelayCommand(CanExecute = nameof(CanExplainFurther))]
    private async Task ExplainFurtherAsync()
    {
        IsExplaining = true;

        try
        {
            var prompt =
                $"Explain this tutorial step in more depth than the explanation below already does. " +
                $"Respond in plain prose only - no JSON, no markdown fences.\n\n" +
                $"Step: {Title}\n\nExplanation so far: {Explanation}\n\nRelevant code:\n{Hunk.Snippet}";

            var result = await _llmRunner.RunPromptAsync(prompt, _workingDirectoryPath, resumeSessionId: _resumeSessionId);

            FurtherExplanation = result.Success
                ? ExtractProseAnswer(result.Output)
                : string.Format("Tutorial_ExplainFurtherFailed".Resolve(), result.ErrorMessage);
        }
        finally
        {
            IsExplaining = false;
        }
    }

    private bool CanExplainFurther() => !IsExplaining;

    // Lets the user ask anything about this step - not just "go deeper" - while still
    // resuming the same session so Claude already has the whole file/tutorial as context.
    [RelayCommand(CanExecute = nameof(CanAskQuestion))]
    private async Task AskQuestionAsync()
    {
        IsAskingQuestion = true;

        try
        {
            var prompt =
                $"Answer this question about the tutorial step below. " +
                $"Respond in plain prose only - no JSON, no markdown fences.\n\n" +
                $"Step: {Title}\n\nExplanation so far: {Explanation}\n\nRelevant code:\n{Hunk.Snippet}\n\n" +
                $"Question: {QuestionText}";

            var result = await _llmRunner.RunPromptAsync(prompt, _workingDirectoryPath, resumeSessionId: _resumeSessionId);

            QuestionAnswer = result.Success
                ? ExtractProseAnswer(result.Output)
                : string.Format("Tutorial_AskQuestionFailed".Resolve(), result.ErrorMessage);
        }
        finally
        {
            IsAskingQuestion = false;
        }
    }

    private bool CanAskQuestion() => !IsAskingQuestion && !string.IsNullOrWhiteSpace(QuestionText);

    // Asked explicitly for plain prose above, but the resumed session's first turn insisted on the
    // JSON contract, so a model that answers in JSON anyway is handled rather than shown raw - .Tutorial
    // doubles as the prose text for both an "answer" and a "development" response (see ClaudeParsedResponse).
    private static string ExtractProseAnswer(string output) =>
        ClaudeJsonResponseParser.TryParse(output, out var response, out _) ? response!.Tutorial : output.Trim();
}
