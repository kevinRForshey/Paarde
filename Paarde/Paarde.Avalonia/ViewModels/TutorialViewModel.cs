using System.Collections.ObjectModel;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Contracts.ViewModels;
using Paarde.Avalonia.Models;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.Strings;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.Models;
using Paarde.Core.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Paarde.Avalonia.ViewModels;

public partial class TutorialViewModel : ObservableRecipient, INavigationAware, IConfirmNavigationAway
{
    private const string StageAppliedFilesInGitSettingsKey = "Tutorial_StageAppliedFilesInGit";

    private readonly IPromptResultStore _promptResultStore;
    private readonly IApplyOrchestrationService _applyOrchestrationService;
    private readonly IApplyHistoryLog _applyHistoryLog;
    private readonly IDialogService _dialogService;
    private readonly ILlmRunner _llmRunner;
    private readonly IPromptTemplateProvider _promptTemplateProvider;
    private readonly IFileSavePickerService _fileSavePickerService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IBuildTestRunner _buildTestRunner;
    private readonly ILogger<TutorialViewModel> _logger;

    // T8.4.5: the step TutorialDetailControl should scroll to once it's materialized for the file a
    // TutorialJumpRequest selected - "take once" (read and cleared by TutorialDetailControl.axaml.cs)
    // so a later, unrelated re-materialization of the same control never re-triggers the scroll.
    private ProposedHunk? _pendingStepTarget;

    // T8.4.1: the `## Tutorial` overview PromptViewModel parses into PromptResultStore.Tutorial but
    // no view previously read - pulled on navigation the same way Files already is, rather than a
    // live PropertyChanged subscription, since this page already re-reads the whole store on every
    // OnNavigatedTo.
    [ObservableProperty]
    private string tutorial = string.Empty;

    [ObservableProperty]
    private ProposedFileChangeViewModel? selected;

    [ObservableProperty]
    private string applyStatusMessage = string.Empty;

    // T4.3.3 follow-up: whether ApplySelectedAsync should `git add` the files it just wrote,
    // persisted (like PromptViewModel.TimeoutSeconds) rather than session-scoped, since the user's
    // preference here is unlikely to change run-to-run. Defaults to true to match T4.3.3's original
    // unconditional behavior for anyone who hasn't touched the checkbox yet.
    [ObservableProperty]
    private bool stageAppliedFilesInGit = true;

    // T7.3.2: the scoped "refine this file" follow-up - a resumed-session request to change only
    // Selected, reusing the same instruction template/parsing pipeline as the main run rather than
    // a bespoke one.
    [ObservableProperty]
    private string refineInstruction = string.Empty;

    [ObservableProperty]
    private string refineStatusMessage = string.Empty;

    [ObservableProperty]
    private bool isRefining;

    // Epic 15, T15.2: no bindable GhostTextSuggestion property here like ExplorerViewModel's -
    // TutorialDetailControl's Editor binds to a ProposedFileChangeViewModel, not this VM, so there's
    // nothing for an AXAML binding to reach. RequestGhostTextCompletionAsync below just returns the
    // suggestion directly and TutorialDetailControl's code-behind assigns it to Editor.GhostTextSuggestion.
    private CancellationTokenSource? _ghostTextCancellationTokenSource;

    // T8.4.4: status for the export-to-file commands below, surfaced the same inline way as
    // ApplyStatusMessage rather than a dialog - exporting isn't a destructive/multi-outcome
    // operation worth interrupting the user for.
    [ObservableProperty]
    private string exportStatusMessage = string.Empty;

    public ObservableCollection<ProposedFileChangeViewModel> Files { get; } = new();

    // T8.2.2: files currently open as tabs in the detail pane - a subset of Files, populated as the
    // user selects files from the master list (see OnSelectedChanged below) rather than all of Files
    // up front, so the page opens with just the one initial tab EnsureItemSelected already picked.
    // Reset on every OnNavigatedTo along with Files/Selected - this view model is resolved fresh
    // (AddTransient) on every navigation to this page, so there's nothing to preserve across a
    // navigate-away-and-back the way there would be if it were a singleton.
    public ObservableCollection<ProposedFileChangeViewModel> OpenTabs { get; } = new();

    // T4.2.3: display-ready rows for the apply-history log, newest first. Populated from
    // IApplyHistoryLog rather than bound to it directly, since that store's Entries is a plain
    // IReadOnlyList<ApplyHistoryEntry> with no change notification the view could bind to.
    public ObservableCollection<string> ApplyHistoryEntries { get; } = new();

    // Drives whether the history panel shows itself or stays collapsed - there's nothing useful to
    // show before the first apply of the session.
    public bool HasApplyHistory => ApplyHistoryEntries.Count > 0;

    public TutorialViewModel(IPromptResultStore promptResultStore, IApplyOrchestrationService applyOrchestrationService, IApplyHistoryLog applyHistoryLog, IDialogService dialogService, ILlmRunner llmRunner, IPromptTemplateProvider promptTemplateProvider, IFileSavePickerService fileSavePickerService, ILocalSettingsService localSettingsService, IBuildTestRunner buildTestRunner, ILogger<TutorialViewModel>? logger = null)
    {
        _promptResultStore = promptResultStore;
        _applyOrchestrationService = applyOrchestrationService;
        _applyHistoryLog = applyHistoryLog;
        _dialogService = dialogService;
        _llmRunner = llmRunner;
        _promptTemplateProvider = promptTemplateProvider;
        _fileSavePickerService = fileSavePickerService;
        _localSettingsService = localSettingsService;
        _buildTestRunner = buildTestRunner;
        _logger = logger ?? NullLogger<TutorialViewModel>.Instance;

        // ObservableCollection<T> raises PropertyChanged("Count") on its own, but HasNoFiles/
        // HasFilesButNoneSelected are derived from that count rather than being Count itself, so
        // the view's bindings to them need an explicit nudge whenever the collection changes.
        Files.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoFiles));
            OnPropertyChanged(nameof(HasFilesButNoneSelected));
            ExportTutorialAsMarkdownCommand.NotifyCanExecuteChanged();
            ExportTutorialAsHtmlCommand.NotifyCanExecuteChanged();
        };

        ApplyHistoryEntries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasApplyHistory));
        OpenTabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOpenTabs));
    }

    // Async void because INavigationAware.OnNavigatedTo returns void - wrapped (same as
    // PromptViewModel.OnNavigatedTo) so a corrupt settings file is logged rather than unwound
    // unobserved out of NavigationService.OnNavigated.
    public async void OnNavigatedTo(object parameter)
    {
        Tutorial = _promptResultStore.Tutorial;

        try
        {
            var savedStageAppliedFilesInGit = await _localSettingsService.ReadSettingAsync<bool?>(StageAppliedFilesInGitSettingsKey);
            StageAppliedFilesInGit = savedStageAppliedFilesInGit ?? true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load saved settings while navigating to the Tutorial page.");
        }

        Files.Clear();
        OpenTabs.Clear();

        foreach (var file in _promptResultStore.Files)
        {
            Files.Add(file);
        }

        // T8.4.5: a jump from the Diffs page's "Back to tutorial" selects its file directly and
        // remembers which step to scroll to, rather than relying on EnsureItemSelected's first-file
        // default below.
        if (parameter is TutorialJumpRequest jump)
        {
            _pendingStepTarget = jump.TargetStep;
            Selected = jump.File;
        }

        // The WinUI version only auto-selects the first file once the master/detail view reports it has
        // room to show both panes side by side; the hand-built two-pane layout here always shows both,
        // so it's safe to select eagerly instead of waiting for a ViewStateChanged-equivalent signal.
        EnsureItemSelected();

        // The log is session-scoped (same singleton across page visits), so re-read it every time
        // this page is shown rather than only right after an apply/undo/revert - otherwise
        // navigating away and back would show a stale list.
        RefreshApplyHistoryEntries();
    }

    public void OnNavigatedFrom()
    {
    }

    public bool HasUnsavedChanges => Files.Any(f => f.IsDirty);

    // Drives the Tutorial page's empty-state messaging: with no files at all (no prompt run yet)
    // the master list is empty and there's nothing to select, which otherwise looks indistinguishable
    // from the page having failed to load.
    public bool HasNoFiles => Files.Count == 0;

    public bool HasFilesButNoneSelected => Files.Count > 0 && Selected == null;

    // Drives whether the tab strip/detail pane shows itself at all - distinct from
    // HasFilesButNoneSelected, which is about there being files to pick from in the first place.
    public bool HasOpenTabs => OpenTabs.Count > 0;

    // Drives whether the tutorial-overview header shows itself - a response with no files (or a
    // plain-text/unstructured response) can still have a non-empty overview worth showing.
    public bool HasTutorialOverview => !string.IsNullOrWhiteSpace(Tutorial);

    partial void OnTutorialChanged(string value)
    {
        OnPropertyChanged(nameof(HasTutorialOverview));
        ExportTutorialAsMarkdownCommand.NotifyCanExecuteChanged();
        ExportTutorialAsHtmlCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedChanged(ProposedFileChangeViewModel? value)
    {
        // T8.2.2: selecting a file from the master list (or a jump/refine elsewhere in this view
        // model) opens it as a tab if it isn't one already, rather than requiring a separate
        // "open" action - ReplaceFile already swaps a refined file into OpenTabs directly, so this
        // is a no-op for that path.
        if (value is not null && !OpenTabs.Contains(value))
        {
            OpenTabs.Add(value);
        }

        OnPropertyChanged(nameof(HasFilesButNoneSelected));
        RefineSelectedFileCommand.NotifyCanExecuteChanged();
        CloseSelectedTabCommand.NotifyCanExecuteChanged();
    }

    partial void OnStageAppliedFilesInGitChanged(bool value) => _localSettingsService.SaveSettingAsync(StageAppliedFilesInGitSettingsKey, value);

    partial void OnRefineInstructionChanged(string value) => RefineSelectedFileCommand.NotifyCanExecuteChanged();

    partial void OnIsRefiningChanged(bool value) => RefineSelectedFileCommand.NotifyCanExecuteChanged();

    public void EnsureItemSelected()
    {
        Selected ??= Files.FirstOrDefault();
    }

    // "Take once" accessor for the step target a TutorialJumpRequest set - called by
    // TutorialDetailControl.axaml.cs once it's materialized for the target file.
    public ProposedHunk? TakePendingStepTarget()
    {
        var target = _pendingStepTarget;
        _pendingStepTarget = null;
        return target;
    }

    // F3.3: applies every selected hunk/whole-file change across every file in the tutorial, not
    // just the one file currently shown in the detail pane. Confirms with a pre-apply summary
    // (T3.3.1) before touching disk, then reports what happened per file (T3.3.3 conflicts,
    // T3.3.4 partial failures) rather than assuming the whole batch succeeded.
    [RelayCommand]
    private async Task ApplySelectedAsync()
    {
        var preview = _applyOrchestrationService.Preview(Files, _promptResultStore.WorkingDirectoryPath);

        if (preview.FileCount == 0)
        {
            ApplyStatusMessage = "Tutorial_ApplySelectedNoneSelected".Resolve();
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "Tutorial_ApplySelectedConfirmTitle".Resolve(),
            string.Format("Tutorial_ApplySelectedConfirmMessage".Resolve(), preview.ChangeCount, preview.FileCount),
            "Tutorial_ApplySelectedConfirmButton".Resolve(),
            "Tutorial_ApplySelectedCancelButton".Resolve());

        if (!confirmed)
        {
            return;
        }

        // Asked separately from the general apply-confirm above (T4.3.2): unlike protected paths,
        // overwriting a file that's already dirty in git isn't something a per-file skip can make
        // safe - there's no commit to fall back on for it either way - so declining here cancels
        // the whole apply rather than just excluding those files from the batch.
        if (preview.IsGitRepository && preview.GitDirtyFileCount > 0)
        {
            var dirtyPaths = string.Join(", ", preview.GitDirtyFiles);
            var proceedDespiteDirtyFiles = await _dialogService.ConfirmAsync(
                "Tutorial_ApplyGitDirtyConfirmTitle".Resolve(),
                string.Format("Tutorial_ApplyGitDirtyConfirmMessage".Resolve(), preview.GitDirtyFileCount, dirtyPaths),
                "Tutorial_ApplyGitDirtyConfirmButton".Resolve(),
                "Tutorial_ApplyGitDirtyConfirmCancelButton".Resolve());

            if (!proceedDespiteDirtyFiles)
            {
                return;
            }
        }

        // Asked separately from the general apply-confirm above (T4.1.2), so declining it only
        // skips the protected files (reported below as ProtectedPathBlockedCount) rather than
        // aborting the whole batch - consistent with T3.3.4's "one file's problem never stops the
        // rest" philosophy.
        var allowProtectedPaths = true;

        if (preview.ProtectedPathCount > 0)
        {
            var protectedPaths = string.Join(", ", preview.ProtectedPathFiles);
            allowProtectedPaths = await _dialogService.ConfirmAsync(
                "Tutorial_ApplyProtectedPathsConfirmTitle".Resolve(),
                string.Format("Tutorial_ApplyProtectedPathsConfirmMessage".Resolve(), preview.ProtectedPathCount, protectedPaths),
                "Tutorial_ApplyProtectedPathsConfirmButton".Resolve(),
                "Tutorial_ApplyProtectedPathsConfirmCancelButton".Resolve());
        }

        var result = _applyOrchestrationService.ApplySelected(Files, _promptResultStore.WorkingDirectoryPath, allowProtectedPaths, StageAppliedFilesInGit);

        var messageParts = new List<string> { string.Format("Tutorial_ApplySelectedResultApplied".Resolve(), result.AppliedCount) };

        if (result.ConflictCount > 0)
        {
            var conflictedPaths = string.Join(", ", result.ConflictedFiles.Select(r => r.FilePath));
            messageParts.Add(string.Format("Tutorial_ApplySelectedResultConflicts".Resolve(), result.ConflictCount, conflictedPaths));
        }

        if (result.FailedCount > 0)
        {
            var failedPaths = string.Join(", ", result.FailedFiles.Select(r => r.FilePath));
            messageParts.Add(string.Format("Tutorial_ApplySelectedResultFailures".Resolve(), result.FailedCount, failedPaths));
        }

        if (result.ProtectedPathBlockedCount > 0)
        {
            var blockedPaths = string.Join(", ", result.ProtectedPathBlockedFiles.Select(r => r.FilePath));
            messageParts.Add(string.Format("Tutorial_ApplySelectedResultProtectedBlocked".Resolve(), result.ProtectedPathBlockedCount, blockedPaths));
        }

        if (result.FilesStagedInGit)
        {
            messageParts.Add("Tutorial_ApplySelectedResultStaged".Resolve());
        }

        // F12.2: only worth checking the build/test commands configured on the Settings page if
        // something actually changed on disk - skipped entirely (rather than just skipping the
        // confirm dialog) on a no-op apply, same as the dirty-files/protected-paths checks above
        // never run for a batch with nothing selected.
        if (result.AppliedCount > 0)
        {
            await RunConfiguredBuildAndTestAsync(messageParts);
        }

        ApplyStatusMessage = string.Join(" ", messageParts);
        RefreshApplyHistoryEntries();

        await _dialogService.InfoAsync(
            "Tutorial_ApplySelectedResultTitle".Resolve(),
            ApplyStatusMessage,
            "Tutorial_ApplySelectedResultClose".Resolve());
    }

    // T12.1.3: a hung build/test command must not hang the whole apply flow - mirrors the safety
    // property (not the UI) of Prompt_RunTimedOut's CLI-run timeout, but fixed rather than a second
    // Settings-configurable value: unlike a long-running agentic CLI turn, a build or test command
    // that's still running after this long is realistically stuck, not just slow.
    private const int BuildTestTimeoutSeconds = 300;

    // F12.2: runs the Settings-page-configured build/test commands (F12.1.2) against the working
    // directory right after an apply, appending their outcome to the same messageParts list
    // ApplySelectedAsync is already building up. A blank command (saved-but-blank, not merely
    // unset - see SettingsViewModel's own comment on this) skips that step entirely; the test
    // command additionally only runs if the build step was skipped or succeeded, since a test run
    // against code that doesn't build isn't a meaningful signal.
    private async Task RunConfiguredBuildAndTestAsync(List<string> messageParts)
    {
        ClearPendingBuildTestFailure();

        var buildCommand = await _localSettingsService.ReadSettingAsync<string>(BuildTestSettingsKeys.BuildCommand);
        var buildFailed = false;

        if (!string.IsNullOrWhiteSpace(buildCommand))
        {
            var buildResult = await RunWithTimeoutAsync(buildCommand);
            buildFailed = buildResult is null || !buildResult.Succeeded;

            if (buildResult is null)
            {
                messageParts.Add(string.Format("Tutorial_ApplySelectedBuildTimedOut".Resolve(), BuildTestTimeoutSeconds));
                SetPendingBuildTestFailure(buildCommand, string.Format("Tutorial_ApplySelectedBuildTimedOut".Resolve(), BuildTestTimeoutSeconds));
            }
            else if (buildResult.Succeeded)
            {
                messageParts.Add(string.Format("Tutorial_ApplySelectedBuildSucceeded".Resolve(), buildResult.Duration.TotalSeconds));
            }
            else
            {
                var output = FirstNonBlank(buildResult.StandardError, buildResult.StandardOutput);
                messageParts.Add(string.Format("Tutorial_ApplySelectedBuildFailed".Resolve(), buildResult.ExitCode, output));
                SetPendingBuildTestFailure(buildCommand, output);
            }
        }

        var testCommand = await _localSettingsService.ReadSettingAsync<string>(BuildTestSettingsKeys.TestCommand);

        if (string.IsNullOrWhiteSpace(testCommand))
        {
            return;
        }

        if (buildFailed)
        {
            messageParts.Add("Tutorial_ApplySelectedTestSkippedBuildFailed".Resolve());
            return;
        }

        var testResult = await RunWithTimeoutAsync(testCommand);

        if (testResult is null)
        {
            messageParts.Add(string.Format("Tutorial_ApplySelectedTestTimedOut".Resolve(), BuildTestTimeoutSeconds));
            SetPendingBuildTestFailure(testCommand, string.Format("Tutorial_ApplySelectedTestTimedOut".Resolve(), BuildTestTimeoutSeconds));
        }
        else if (testResult.Succeeded)
        {
            messageParts.Add(string.Format("Tutorial_ApplySelectedTestSucceeded".Resolve(), testResult.Duration.TotalSeconds));
        }
        else
        {
            var output = FirstNonBlank(testResult.StandardError, testResult.StandardOutput);
            messageParts.Add(string.Format("Tutorial_ApplySelectedTestFailed".Resolve(), testResult.ExitCode, output));
            SetPendingBuildTestFailure(testCommand, output);
        }
    }

    // Null means "timed out" - RunAsync's own contract (see IBuildTestRunner) only throws
    // OperationCanceledException for a cancellation/timeout, never for an ordinary command failure,
    // so that's the only exception worth catching here.
    private async Task<CommandResult?> RunWithTimeoutAsync(string command)
    {
        using var timeoutCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(BuildTestTimeoutSeconds));

        try
        {
            return await _buildTestRunner.RunAsync(command, _promptResultStore.WorkingDirectoryPath, timeoutCancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static string FirstNonBlank(string preferred, string fallback) => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    // T12.2.2: lets SendBuildTestErrorsToClaudeCommand act on the most recent build/test failure -
    // cleared at the start of every RunConfiguredBuildAndTestAsync call so a stale failure from an
    // earlier apply never lingers once a newer one has run (whether or not that newer run failed
    // too; a fresh SetPendingBuildTestFailure call right after this one re-populates it if it did).
    private string? _pendingBuildTestFailureCommand;
    private string? _pendingBuildTestFailureOutput;

    public bool HasPendingBuildTestFailure => _pendingBuildTestFailureOutput is not null;

    private void SetPendingBuildTestFailure(string command, string output)
    {
        _pendingBuildTestFailureCommand = command;
        _pendingBuildTestFailureOutput = output;
        OnPropertyChanged(nameof(HasPendingBuildTestFailure));
        SendBuildTestErrorsToClaudeCommand.NotifyCanExecuteChanged();
    }

    private void ClearPendingBuildTestFailure()
    {
        _pendingBuildTestFailureCommand = null;
        _pendingBuildTestFailureOutput = null;
        OnPropertyChanged(nameof(HasPendingBuildTestFailure));
        SendBuildTestErrorsToClaudeCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    private bool isSendingBuildTestErrorsToClaude;

    [ObservableProperty]
    private string sendBuildTestErrorsStatusMessage = string.Empty;

    partial void OnIsSendingBuildTestErrorsToClaudeChanged(bool value) => SendBuildTestErrorsToClaudeCommand.NotifyCanExecuteChanged();

    // T12.2.2/T12.2.3: builds a follow-up prompt from the captured build/test failure and resumes
    // the same CLI conversation (T7.3.1), the same resumeSessionId approach RefineSelectedFileAsync
    // already uses - Claude already has the original instruction/tutorial in that conversation's
    // history, so this only needs to describe what broke, not restate the whole original ask. Any
    // corrected files it returns flow through StructuredResponseParser and merge into Files the
    // same way a normal run's response does - no second, unreviewed write path (T12.2.3).
    [RelayCommand(CanExecute = nameof(CanSendBuildTestErrorsToClaude))]
    private async Task SendBuildTestErrorsToClaudeAsync()
    {
        var command = _pendingBuildTestFailureCommand!;
        var output = _pendingBuildTestFailureOutput!;

        IsSendingBuildTestErrorsToClaude = true;
        SendBuildTestErrorsStatusMessage = "Tutorial_SendBuildTestErrorsRunning".Resolve();

        try
        {
            var prompt = _promptTemplateProvider.GetTutorialInstructionPrompt() +
                $"\n\nRunning `{command}` in the working directory failed with this output:\n{output}" +
                "\n\nFix the problem and respond with the corrected file(s) in the same JSON format." +
                $"\n\nThe working directory is: {_promptResultStore.WorkingDirectoryPath}";

            var result = await _llmRunner.RunPromptAsync(prompt, _promptResultStore.WorkingDirectoryPath, resumeSessionId: _promptResultStore.SessionId);

            if (!result.Success)
            {
                SendBuildTestErrorsStatusMessage = string.Format("Tutorial_SendBuildTestErrorsFailed".Resolve(), result.ErrorMessage);
                return;
            }

            var parseResult = StructuredResponseParser.Parse(result.Output, _promptResultStore.WorkingDirectoryPath);

            if (parseResult.UsedFallback || parseResult.Response.Files.Count == 0)
            {
                SendBuildTestErrorsStatusMessage = "Tutorial_SendBuildTestErrorsNoFiles".Resolve();
                return;
            }

            foreach (var correctedFile in parseResult.Response.Files)
            {
                // Content-mode files come back with OriginalContent unset - see
                // ClaudeJsonResponseParser.MapContentModeFile - same re-diff RefineSelectedFileAsync
                // already does for the same reason.
                correctedFile.RefreshOriginalContent(_promptResultStore.WorkingDirectoryPath);

                var existingFile = Files.FirstOrDefault(f => f.FilePath == correctedFile.FilePath);

                if (existingFile is not null)
                {
                    ReplaceFile(existingFile, correctedFile);
                }
                else
                {
                    Files.Add(correctedFile);
                    _promptResultStore.Files.Add(correctedFile);
                }
            }

            ClearPendingBuildTestFailure();
            SendBuildTestErrorsStatusMessage = string.Format("Tutorial_SendBuildTestErrorsSucceeded".Resolve(), parseResult.Response.Files.Count);
        }
        finally
        {
            IsSendingBuildTestErrorsToClaude = false;
        }
    }

    private bool CanSendBuildTestErrorsToClaude() => HasPendingBuildTestFailure && !IsSendingBuildTestErrorsToClaude;

    // T4.2.2: reverts the files touched by the most recent ApplySelected batch to their content
    // from immediately before that batch, confirming first since it overwrites whatever's on disk
    // now. Nothing-to-undo is reported the same way ApplySelectedAsync reports nothing-selected -
    // inline via ApplyStatusMessage - rather than disabling the command, consistent with how this
    // page already handles that case.
    [RelayCommand]
    private async Task UndoLastApplyAsync()
    {
        var confirmed = await _dialogService.ConfirmAsync(
            "Tutorial_UndoLastApplyConfirmTitle".Resolve(),
            "Tutorial_UndoLastApplyConfirmMessage".Resolve(),
            "Tutorial_UndoLastApplyConfirmButton".Resolve(),
            "Tutorial_UndoLastApplyCancelButton".Resolve());

        if (!confirmed)
        {
            return;
        }

        if (!_applyOrchestrationService.TryUndoLastApply(_promptResultStore.WorkingDirectoryPath, out var result))
        {
            ApplyStatusMessage = "Tutorial_UndoLastApplyNothingToUndo".Resolve();
            return;
        }

        RefreshFilesAffectedBy(result);
        ApplyStatusMessage = BuildRevertStatusMessage(result);
        RefreshApplyHistoryEntries();

        await _dialogService.InfoAsync(
            "Tutorial_UndoLastApplyResultTitle".Resolve(),
            ApplyStatusMessage,
            "Tutorial_UndoLastApplyResultClose".Resolve());
    }

    // T4.2.2: reverts every file this session has applied a change to, back to its content from
    // before this session touched it at all - not just the most recent batch, so this is confirmed
    // separately and more strongly worded than UndoLastApplyAsync.
    [RelayCommand]
    private async Task RevertAllAppliedAsync()
    {
        var confirmed = await _dialogService.ConfirmAsync(
            "Tutorial_RevertAllAppliedConfirmTitle".Resolve(),
            "Tutorial_RevertAllAppliedConfirmMessage".Resolve(),
            "Tutorial_RevertAllAppliedConfirmButton".Resolve(),
            "Tutorial_RevertAllAppliedCancelButton".Resolve());

        if (!confirmed)
        {
            return;
        }

        var result = _applyOrchestrationService.RevertAllApplied(_promptResultStore.WorkingDirectoryPath);

        if (result.Results.Count == 0)
        {
            ApplyStatusMessage = "Tutorial_RevertAllAppliedNothingToRevert".Resolve();
            return;
        }

        RefreshFilesAffectedBy(result);
        ApplyStatusMessage = BuildRevertStatusMessage(result);
        RefreshApplyHistoryEntries();

        await _dialogService.InfoAsync(
            "Tutorial_RevertAllAppliedResultTitle".Resolve(),
            ApplyStatusMessage,
            "Tutorial_RevertAllAppliedResultClose".Resolve());
    }

    // T7.3.2: a scoped follow-up that only asks Claude to change Selected, resuming the conversation
    // that produced the current result (T7.3.1) so it already has the whole tutorial as context.
    // Reuses the main run's instruction template/JSON contract and StructuredResponseParser rather
    // than a bespoke prompt/parse path, so a successful response is just another file in the same
    // shape Files already holds - deliberately no schema-validation retry loop here (unlike the main
    // run): a failed attempt just reports an error and the user can click the button again.
    [RelayCommand(CanExecute = nameof(CanRefineSelectedFile))]
    private async Task RefineSelectedFileAsync()
    {
        var file = Selected!;
        IsRefining = true;
        RefineStatusMessage = string.Format("Tutorial_RefineRunning".Resolve(), file.FilePath);

        try
        {
            var prompt = _promptTemplateProvider.GetTutorialInstructionPrompt() +
                $"\n\nOnly modify the file \"{file.FilePath}\" - do not include any other file in your response. Its current content is:\n{file.EditedContent}" +
                $"\n\nApply this refinement: {RefineInstruction}" +
                $"\n\nThe working directory is: {_promptResultStore.WorkingDirectoryPath}";

            var result = await _llmRunner.RunPromptAsync(prompt, _promptResultStore.WorkingDirectoryPath, resumeSessionId: _promptResultStore.SessionId);

            if (!result.Success)
            {
                RefineStatusMessage = string.Format("Tutorial_RefineFailed".Resolve(), file.FilePath, result.ErrorMessage);
                return;
            }

            var parseResult = StructuredResponseParser.Parse(result.Output, _promptResultStore.WorkingDirectoryPath);
            var refinedFile = parseResult.Response.Files.FirstOrDefault(f => f.FilePath == file.FilePath);

            if (parseResult.UsedFallback || refinedFile is null)
            {
                RefineStatusMessage = string.Format("Tutorial_RefineNoMatchingFile".Resolve(), file.FilePath);
                return;
            }

            // Content-mode files (unlike edit-mode) come back with OriginalContent unset - see
            // ClaudeJsonResponseParser.MapContentModeFile - so this re-diffs against disk the same
            // way DiffsViewModel.OnSelectedChanged does for every file it shows.
            refinedFile.RefreshOriginalContent(_promptResultStore.WorkingDirectoryPath);
            ReplaceFile(file, refinedFile);

            RefineInstruction = string.Empty;
            RefineStatusMessage = string.Format("Tutorial_RefineSucceeded".Resolve(), refinedFile.FilePath);
        }
        finally
        {
            IsRefining = false;
        }
    }

    private bool CanRefineSelectedFile() => Selected is not null && !string.IsNullOrWhiteSpace(RefineInstruction) && !IsRefining;

    // Epic 15, T15.2.1/T15.2.2: handles TutorialDetailControl's Editor.GhostTextRequested, forwarded
    // up via FindAncestorOfType<TutorialView>().ViewModel the same way ApplyPendingStepTarget already
    // reaches this page-level view model - Editor's DataContext is the per-file
    // ProposedFileChangeViewModel, which has no ILlmRunner (deliberately - see its own doc comment),
    // so this is the nearest VM that actually owns one. Same reused-runner/no-dedicated-fast-model
    // tradeoff as ExplorerViewModel.RequestGhostTextCompletionAsync; see its comment for why. No
    // resumeSessionId - a one-off completion lookup, not a continuation of the main run's conversation.
    public async Task<string?> RequestGhostTextCompletionAsync(string textBeforeCaret, string textAfterCaret)
    {
        if (Selected is not { } file)
        {
            return null;
        }

        _ghostTextCancellationTokenSource?.Cancel();
        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        _ghostTextCancellationTokenSource = cancellationTokenSource;

        try
        {
            var prompt =
                "Complete the code at the <cursor/> marker below. Respond with ONLY the text that " +
                "should be inserted at the cursor - no explanation, no markdown fences, nothing before " +
                "or after it. If nothing reasonable completes the code there, respond with nothing.\n\n" +
                $"File: {file.FilePath}\n\n{textBeforeCaret}<cursor/>{textAfterCaret}";

            var result = await _llmRunner.RunPromptAsync(prompt, _promptResultStore.WorkingDirectoryPath, cancellationToken: cancellationTokenSource.Token);
            if (cancellationTokenSource.IsCancellationRequested || !result.Success || string.IsNullOrWhiteSpace(result.Output))
            {
                return null;
            }

            return result.Output.Trim();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request, or timed out - either way, there's nothing to show.
            return null;
        }
    }

    // T8.2.2: closes the active tab - a single command acting on Selected rather than a close
    // affordance on each tab header, consistent with how this page already drives per-row actions
    // (ApplySelected/Fork/Rerun etc. elsewhere in this codebase act on a Selected item rather than a
    // command bound per-row back out to this view model's DataContext from inside a DataTemplate).
    [RelayCommand(CanExecute = nameof(CanCloseSelectedTab))]
    private void CloseSelectedTab()
    {
        if (Selected is not { } file)
        {
            return;
        }

        var index = OpenTabs.IndexOf(file);
        OpenTabs.Remove(file);

        // Land on a neighboring tab rather than clearing the detail pane entirely while other tabs
        // are still open; Min guards the case where the closed tab was the last one in the list.
        Selected = OpenTabs.Count == 0 ? null : OpenTabs[Math.Min(index, OpenTabs.Count - 1)];
    }

    private bool CanCloseSelectedTab() => Selected is not null;

    // T8.4.4: builds a self-contained document (TutorialExporter, Core) a user can save and share
    // outside the app - Markdown and HTML share this one picker-then-write flow, differing only in
    // which exporter/file-type/extension they pass in.
    [RelayCommand(CanExecute = nameof(CanExportTutorial))]
    private Task ExportTutorialAsMarkdownAsync() =>
        ExportTutorialAsync("Tutorial_ExportAsMarkdownTitle".Resolve(), "tutorial.md", "Markdown", "md", TutorialExporter.BuildMarkdown);

    [RelayCommand(CanExecute = nameof(CanExportTutorial))]
    private Task ExportTutorialAsHtmlAsync() =>
        ExportTutorialAsync("Tutorial_ExportAsHtmlTitle".Resolve(), "tutorial.html", "HTML", "html", TutorialExporter.BuildHtml);

    private bool CanExportTutorial() => Files.Count > 0 || HasTutorialOverview;

    private async Task ExportTutorialAsync(string title, string suggestedFileName, string fileTypeName, string extension, Func<string, IReadOnlyList<ProposedFileChangeViewModel>, string> buildContent)
    {
        var path = await _fileSavePickerService.PickSaveFileAsync(title, suggestedFileName, fileTypeName, [extension]);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.WriteAllText(path, buildContent(Tutorial, Files));
            ExportStatusMessage = string.Format("Tutorial_ExportSucceeded".Resolve(), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to export the tutorial to {Path}.", path);
            ExportStatusMessage = string.Format("Tutorial_ExportFailed".Resolve(), ex.Message);
        }
    }

    // Files/IPromptResultStore.Files both hold the same instances (see OnNavigatedTo), and
    // ProposedFileChangeViewModel can't be mutated into a new file's shape in place (Hunks is
    // init-only, ProposedContent has no setter) - so a refined file is swapped in by index in both
    // collections instead, keeping the Diffs page in sync without any new pub/sub wiring.
    private void ReplaceFile(ProposedFileChangeViewModel oldFile, ProposedFileChangeViewModel newFile)
    {
        var filesIndex = Files.IndexOf(oldFile);

        if (filesIndex >= 0)
        {
            Files[filesIndex] = newFile;
        }

        var storeIndex = _promptResultStore.Files.IndexOf(oldFile);

        if (storeIndex >= 0)
        {
            _promptResultStore.Files[storeIndex] = newFile;
        }

        // Swaps the refined file into the same tab position rather than letting OnSelectedChanged's
        // add-if-missing logic (below, via Selected = newFile) append it as a brand new tab while
        // leaving oldFile's now-stale tab behind.
        var tabIndex = OpenTabs.IndexOf(oldFile);

        if (tabIndex >= 0)
        {
            OpenTabs[tabIndex] = newFile;
        }

        Selected = newFile;
    }

    private string BuildRevertStatusMessage(RevertResult result)
    {
        var messageParts = new List<string> { string.Format("Tutorial_RevertResultSucceeded".Resolve(), result.SucceededCount) };

        if (result.FailedCount > 0)
        {
            var failedPaths = string.Join(", ", result.FailedFiles.Select(r => r.FilePath));
            messageParts.Add(string.Format("Tutorial_RevertResultFailures".Resolve(), result.FailedCount, failedPaths));
        }

        return string.Join(" ", messageParts);
    }

    // Writes back by TryUndoLastApply/RevertAllApplied happen directly against disk, with no
    // reference to the live ProposedFileChangeViewModel instances this page is showing - so each
    // successfully reverted file's OriginalContent/hunks need an explicit refresh afterward, the
    // same re-diff RefreshOriginalContent already does for a T3.3.3 conflict.
    private void RefreshFilesAffectedBy(RevertResult result)
    {
        var revertedPaths = result.Results.Where(r => r.Succeeded).Select(r => r.FilePath).ToHashSet();

        foreach (var file in Files.Where(f => revertedPaths.Contains(f.FilePath)))
        {
            file.RefreshOriginalContent(_promptResultStore.WorkingDirectoryPath);
        }
    }

    // T4.2.3: re-reads IApplyHistoryLog.Entries (oldest first) and rebuilds the display list
    // newest first, so the most recent apply/undo/revert always shows at the top.
    private void RefreshApplyHistoryEntries()
    {
        ApplyHistoryEntries.Clear();

        foreach (var entry in _applyHistoryLog.Entries.Reverse())
        {
            ApplyHistoryEntries.Add(FormatHistoryEntry(entry));
        }
    }

    private static string FormatHistoryEntry(ApplyHistoryEntry entry)
    {
        var timestamp = entry.Timestamp.ToLocalTime().ToString("g");
        var key = entry.Kind switch
        {
            ApplyHistoryEntryKind.Applied => "Tutorial_HistoryEntryApplied",
            ApplyHistoryEntryKind.UndoLastApply => "Tutorial_HistoryEntryUndoLastApply",
            ApplyHistoryEntryKind.RevertAllApplied => "Tutorial_HistoryEntryRevertAllApplied",
            _ => throw new ArgumentOutOfRangeException(nameof(entry), entry.Kind, "Unknown apply-history entry kind."),
        };

        return string.Format(key.Resolve(), timestamp, entry.FileCount);
    }
}
