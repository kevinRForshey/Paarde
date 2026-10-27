using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;

using Avalonia.Threading;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Contracts.ViewModels;
using Paarde.Avalonia.Models;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.Strings;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Paarde.Avalonia.ViewModels;

public partial class PromptViewModel : ObservableRecipient, INavigationAware
{
    private const string PromptSettingsKey = "SavedPrompt";
    private const string TimeoutSecondsSettingsKey = "PromptTimeoutSeconds";
    private const string PresetSettingsKey = "SavedPromptPreset";
    private const string DepthSettingsKey = "SavedTutorialDepth";
    private const string ContextPathsSettingsKey = "SavedContextPaths";
    private const int DefaultTimeoutSeconds = 300;

    // T8.2.4: keeps the quick-switch list to a glance-able size, same rationale/value as
    // MainViewModel.MaxRecentProjects (T8.2.1's dashboard list this mirrors).
    private const int MaxRecentWorkingDirectories = 5;

    // T5.3.1: the instructions prepended to every prompt sent to Claude Code used to live here as a
    // hard-coded const; now it's Paarde.Core/Prompts/TutorialInstructionPromptV1.txt,
    // loaded through IPromptTemplateProvider so the schema contract can change (a new versioned
    // .txt file) without touching this ViewModel, and so T5.3.2 can later let a user override it
    // from Settings behind the same interface. The response contract is a single JSON object
    // (Paarde.Core.Models.ClaudeJsonResponse) instead of markdown headers, parsed by
    // Paarde.Core.Helpers.ClaudeJsonResponseParser; the old markdown parser
    // (ClaudeResponseParser) is kept only as a last-resort fallback (T5.1.3) via
    // StructuredResponseParser.
    private const string RetryInstructionSuffix =
        "\n\nYour previous response could not be parsed: {0}\n\nRespond again with ONLY a single corrected JSON object matching the schema above - no markdown fences, no commentary.";

    private readonly ILlmRunner _llmRunner;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IFilePickerService _filePickerService;
    private readonly IPromptResultStore _promptResultStore;
    private readonly IPromptTemplateProvider _promptTemplateProvider;
    private readonly IRunSessionStore _runSessionStore;
    private readonly ICodebaseIndexService _codebaseIndexService;
    private readonly IWorkingDirectoryContext _workingDirectoryContext;
    private readonly ILogger<PromptViewModel> _logger;

    [ObservableProperty]
    private string promptText = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string cliOutput = string.Empty;

    // Cheap, O(1)-per-delta progress indicator shown while a run is in flight (see OnTextDelta
    // below) - deliberately just a character count rather than the raw text itself, since the
    // user isn't meant to read Claude's streamed output live, only see that something is happening.
    [ObservableProperty]
    private string streamProgressDisplay = string.Empty;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private string workingDirectoryPath = string.Empty;

    // T8.2.4: fast project switching without leaving this page - MainViewModel's Recent Projects
    // (T8.2.1) only helps when landing on the Main page, not while already here. Selecting an
    // item just switches WorkingDirectoryPath and resets back to null (see
    // OnSelectedRecentWorkingDirectoryChanged) rather than staying "selected", since the real
    // current value is already shown in the working-directory text box above.
    public ObservableCollection<string> RecentWorkingDirectories { get; } = new();

    public bool HasRecentWorkingDirectories => RecentWorkingDirectories.Count > 0;

    [ObservableProperty]
    private string? selectedRecentWorkingDirectory;

    [ObservableProperty]
    private int timeoutSeconds = DefaultTimeoutSeconds;

    // T5.3.3: named presets/depth controls that append extra guidance onto the instruction
    // preamble (default or user-overridden, T5.3.2) ahead of the user's own prompt text.
    [ObservableProperty]
    private PromptPreset selectedPreset = PromptPreset.General;

    [ObservableProperty]
    private TutorialDepth selectedDepth = TutorialDepth.Standard;

    public IReadOnlyList<PromptPreset> AvailablePresets { get; } = Enum.GetValues<PromptPreset>();

    public IReadOnlyList<TutorialDepth> AvailableDepths { get; } = Enum.GetValues<TutorialDepth>();

    // T5.4.1: files/folders the user attaches as extra context. A folder is read non-recursively -
    // only the files directly inside it - so attaching one can't silently sweep in .git/bin/obj/
    // node_modules; the user can attach a subfolder explicitly if they want more. Unreadable/binary
    // files are skipped when building the prompt (see BuildContextFilesBlock) rather than failing
    // the whole run.
    public ObservableCollection<string> AttachedPaths { get; } = new();

    [ObservableProperty]
    private string? selectedAttachedPath;

    // T14.1.1: the prompt TextBox's `@`-mention popup content, driven by MentionTriggerDetector
    // (code-behind, since it needs Text/CaretIndex) calling UpdateMentionSuggestions below. Empty
    // means the popup is closed - there's no separate "is it open" flag to fall out of sync with
    // this list.
    public ObservableCollection<string> MentionSuggestions { get; } = new();

    public bool HasMentionSuggestions => MentionSuggestions.Count > 0;

    // T14.2.3: an opt-in "suggest relevant files" step, populated only when the user clicks
    // SuggestRelevantFilesCommand - never auto-injected. Separate from MentionSuggestions, which
    // is the @-trigger popup's own state and has different lifecycle (closes on every keystroke
    // outside a mention) and selection semantics (one pick closes it; here each suggestion can be
    // attached independently, or the whole list dismissed).
    public ObservableCollection<string> SuggestedRelevantFiles { get; } = new();

    public bool HasSuggestedRelevantFiles => SuggestedRelevantFiles.Count > 0;

    // T14.1.1: built lazily from WorkingDirectoryPath (same FileTreeBuilder walk Explorer uses, so
    // gitignored/`.git` entries never show up as mention candidates) and cached across keystrokes -
    // invalidated only when WorkingDirectoryPath itself changes (see OnWorkingDirectoryPathChanged),
    // not on every suggestion request, since re-walking the tree per keystroke would make typing
    // feel laggy on a large repo.
    private FileTreeEntry? _cachedMentionFileTree;
    private string? _cachedMentionFileTreeWorkingDirectory;

    // T6.2.3: live "Xs" readout ticking while a run is in progress, and a post-run cost/token
    // summary once the CLI's final result line has been parsed.
    [ObservableProperty]
    private string elapsedTimeDisplay = string.Empty;

    [ObservableProperty]
    private string usageSummary = string.Empty;

    // Epic 11 (F11.2.2): one entry per completed read-only tool call during the current run,
    // cleared at the start of each run - same "Expander, visible only once non-empty" pattern
    // ExplorerViewModel.FileOverview (T10.1.4) uses, not a new control. Empty whenever
    // AgentToolsEnabled is off, since onToolActivity is then simply never invoked.
    public ObservableCollection<string> ToolActivityLog { get; } = new();

    public bool HasToolActivity => ToolActivityLog.Count > 0;

    // T7.3.1: lets the next run ask the CLI to resume this one's conversation instead of starting
    // a fresh one, so a follow-up prompt carries the prior turn's context. HasPreviousSession
    // gates the checkbox - there's nothing to continue until a run has actually returned a session
    // id, and a working directory change invalidates it (see OnWorkingDirectoryPathChanged), since
    // the CLI's session is scoped to the directory it ran in.
    [ObservableProperty]
    private bool continuePreviousConversation;

    [ObservableProperty]
    private bool hasPreviousSession;

    private string? _lastSessionId;

    // T5.4.2: a live "~N tokens" estimate of the exact prompt BuildFullPromptAsync would send,
    // recomputed whenever something that feeds into it changes (text, working directory, preset/
    // depth, attachments) - so the user has a size sense before clicking Send, not just after.
    [ObservableProperty]
    private string estimatedTokenCountDisplay = string.Empty;

    // Guards against an older recompute's settings read finishing after a newer one and
    // overwriting EstimatedTokenCountDisplay with stale data - a real risk here (unlike most other
    // settings reads in this class) since typing triggers a recompute on every keystroke.
    private int _tokenEstimateVersion;

    // T6.1.1: lives only for the duration of one RunPromptAsync call, so CancelPromptCommand has
    // something to cancel and the run's own cleanup has something to dispose - null the rest of
    // the time (including CanCancelPrompt's guard) rather than relying solely on IsRunning, in case
    // a run finishes and clears it in the same moment the user clicks Cancel.
    private CancellationTokenSource? _runCancellationTokenSource;

    // T6.1.2: cancels independently of _runCancellationTokenSource so RunPromptAsync's catch block
    // can tell *why* the run stopped - checking which of the two sources actually requested
    // cancellation - rather than guessing from a single shared token.
    private CancellationTokenSource? _timeoutCancellationTokenSource;

    // T6.2.3: ticks ElapsedTimeDisplay once a second while a run is in progress.
    private DispatcherTimer? _elapsedTimer;

    private Stopwatch? _runStopwatch;

    public PromptViewModel(ILlmRunner llmRunner, ILocalSettingsService localSettingsService, IFolderPickerService folderPickerService, IFilePickerService filePickerService, IPromptResultStore promptResultStore, IPromptTemplateProvider promptTemplateProvider, IRunSessionStore runSessionStore, ICodebaseIndexService codebaseIndexService, IWorkingDirectoryContext workingDirectoryContext, ILogger<PromptViewModel>? logger = null)
    {
        _llmRunner = llmRunner;
        _localSettingsService = localSettingsService;
        _folderPickerService = folderPickerService;
        _filePickerService = filePickerService;
        _promptResultStore = promptResultStore;
        _promptTemplateProvider = promptTemplateProvider;
        _runSessionStore = runSessionStore;
        _codebaseIndexService = codebaseIndexService;
        _workingDirectoryContext = workingDirectoryContext;
        _logger = logger ?? NullLogger<PromptViewModel>.Instance;

        // T5.4.2: ObservableCollection<T> doesn't go through an [ObservableProperty]'s On*Changed
        // hook, so attachments need their own recompute trigger here instead.
        AttachedPaths.CollectionChanged += (_, _) => UpdateEstimatedTokenCountDisplay();
        RecentWorkingDirectories.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentWorkingDirectories));
        ToolActivityLog.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasToolActivity));
        MentionSuggestions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMentionSuggestions));
        SuggestedRelevantFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSuggestedRelevantFiles));
    }

    // T9.2.3: async void because INavigationAware.OnNavigatedTo returns void - wrapped so a
    // corrupt settings file (the one thing every ReadSettingAsync call here can actually throw
    // on) is logged and reported via StatusMessage rather than unwound unobserved out of
    // NavigationService.OnNavigated. RunPromptCommand.ExecuteAsync's own run (T7.2.3's
    // launchRequest.AutoRun path) already has its own broad catch (see RunPromptAsync below), so
    // it can't itself be the source of what this catches.
    public async void OnNavigatedTo(object parameter)
    {
        try
        {
            PromptText = await _localSettingsService.ReadSettingAsync<string>(PromptSettingsKey) ?? string.Empty;
            WorkingDirectoryPath = await _localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory) ?? string.Empty;

            var savedTimeoutSeconds = await _localSettingsService.ReadSettingAsync<int>(TimeoutSecondsSettingsKey);
            TimeoutSeconds = savedTimeoutSeconds > 0 ? savedTimeoutSeconds : DefaultTimeoutSeconds;

            var savedPreset = await _localSettingsService.ReadSettingAsync<string>(PresetSettingsKey);
            SelectedPreset = Enum.TryParse<PromptPreset>(savedPreset, out var preset) ? preset : PromptPreset.General;

            var savedDepth = await _localSettingsService.ReadSettingAsync<string>(DepthSettingsKey);
            SelectedDepth = Enum.TryParse<TutorialDepth>(savedDepth, out var depth) ? depth : TutorialDepth.Standard;

            var savedContextPaths = await _localSettingsService.ReadSettingAsync<List<string>>(ContextPathsSettingsKey);
            AttachedPaths.Clear();
            foreach (var path in savedContextPaths ?? [])
            {
                AttachedPaths.Add(path);
            }

            StatusMessage = string.Empty;

            // T8.2.4: newest-first (IRunSessionStore.ListSessions' own contract), same
            // Distinct()-keeps-most-recent approach as MainViewModel's Recent Projects.
            RecentWorkingDirectories.Clear();
            foreach (var path in _runSessionStore.ListSessions()
                .Select(session => session.WorkingDirectoryPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct()
                .Take(MaxRecentWorkingDirectories))
            {
                RecentWorkingDirectories.Add(path);
            }

            // T7.2.3: a launch from the History page overrides whatever was just loaded from settings
            // above with the prompt/working directory of the session being re-run or forked - Re-run
            // additionally sends it immediately, same as if the user had clicked RunPromptCommand
            // themselves, guarded by the same CanRunPrompt the button itself respects.
            if (parameter is PromptLaunchRequest launchRequest)
            {
                PromptText = launchRequest.PromptText;
                WorkingDirectoryPath = launchRequest.WorkingDirectoryPath;

                if (launchRequest.AutoRun && RunPromptCommand.CanExecute(null))
                {
                    await RunPromptCommand.ExecuteAsync(null);
                }
            }

            UpdateEstimatedTokenCountDisplay();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load saved settings while navigating to the Prompt page.");
            StatusMessage = string.Format("Prompt_LoadSettingsFailed".Resolve(), ex.Message);
        }
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private async Task SavePromptAsync()
    {
        await _localSettingsService.SaveSettingAsync(PromptSettingsKey, PromptText);

        StatusMessage = "Prompt_Saved".Resolve();
    }

    [RelayCommand]
    private async Task BrowseWorkingDirectoryAsync()
    {
        var folder = await _folderPickerService.PickFolderAsync();

        if (folder != null)
        {
            WorkingDirectoryPath = folder;
            await _localSettingsService.SaveSettingAsync(WorkingDirectorySettingsKeys.WorkingDirectory, WorkingDirectoryPath);
        }
    }

    [RelayCommand]
    private async Task AddContextFilesAsync()
    {
        var files = await _filePickerService.PickFilesAsync();
        AddAttachedPaths(files);
        await SaveAttachedPathsAsync();
    }

    [RelayCommand]
    private async Task AddContextFolderAsync()
    {
        var folder = await _folderPickerService.PickFolderAsync();

        if (folder != null)
        {
            AddAttachedPaths([folder]);
            await SaveAttachedPathsAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveAttachedPath))]
    private async Task RemoveAttachedPathAsync()
    {
        if (SelectedAttachedPath is null)
        {
            return;
        }

        AttachedPaths.Remove(SelectedAttachedPath);
        SelectedAttachedPath = null;
        await SaveAttachedPathsAsync();
    }

    private bool CanRemoveAttachedPath() => SelectedAttachedPath is not null;

    private void AddAttachedPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!AttachedPaths.Contains(path))
            {
                AttachedPaths.Add(path);
            }
        }
    }

    private Task SaveAttachedPathsAsync() => _localSettingsService.SaveSettingAsync(ContextPathsSettingsKey, AttachedPaths.ToList());

    // T14.1.1: called by the view's code-behind on every keystroke once MentionTriggerDetector
    // says the caret is inside an `@mention` - a null/blank WorkingDirectoryPath (nothing to
    // search) or a missing directory (stale/typo'd path) just closes the popup rather than
    // throwing, since there's no set of candidate files to show either way.
    public void UpdateMentionSuggestions(string query)
    {
        if (string.IsNullOrWhiteSpace(WorkingDirectoryPath) || !Directory.Exists(WorkingDirectoryPath))
        {
            MentionSuggestions.Clear();
            return;
        }

        if (_cachedMentionFileTree is null || _cachedMentionFileTreeWorkingDirectory != WorkingDirectoryPath)
        {
            _cachedMentionFileTree = FileTreeBuilder.Build(WorkingDirectoryPath);
            _cachedMentionFileTreeWorkingDirectory = WorkingDirectoryPath;
        }

        var matches = FileMentionMatcher.FindMatches(_cachedMentionFileTree, query);

        MentionSuggestions.Clear();
        foreach (var match in matches)
        {
            MentionSuggestions.Add(match);
        }
    }

    public void CloseMentionPopup() => MentionSuggestions.Clear();

    // T14.2.1/T14.2.3: the opt-in "suggest relevant files" step - ranks the working directory's
    // files against the current PromptText via ICodebaseIndexService (TF-IDF, not embeddings; see
    // TfIdfCodebaseIndexService) and lists them for the user to accept individually. Files already
    // attached are filtered out since suggesting them again would be noise, not news.
    [RelayCommand(CanExecute = nameof(CanSuggestRelevantFiles))]
    private async Task SuggestRelevantFilesAsync()
    {
        var alreadyAttached = AttachedPaths
            .Select(path => Path.GetFullPath(Path.Combine(WorkingDirectoryPath, path)))
            .ToHashSet();

        var matches = await _codebaseIndexService.FindRelevantFilesAsync(WorkingDirectoryPath, PromptText);

        SuggestedRelevantFiles.Clear();
        foreach (var match in matches)
        {
            var fullPath = Path.GetFullPath(Path.Combine(WorkingDirectoryPath, match.RelativePath));
            if (!alreadyAttached.Contains(fullPath))
            {
                SuggestedRelevantFiles.Add(match.RelativePath);
            }
        }
    }

    private bool CanSuggestRelevantFiles() => !string.IsNullOrWhiteSpace(WorkingDirectoryPath) && !string.IsNullOrWhiteSpace(PromptText);

    // T14.2.3: accepting one suggestion attaches it (same mechanism AttachMentionedFile already
    // uses) and removes just that entry - the rest of the list stays up so the user can accept
    // more than one without re-running the suggestion step.
    public void AttachSuggestedFile(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(WorkingDirectoryPath, relativePath));
        AddAttachedPaths([fullPath]);
        _ = SaveAttachedPathsAsync();
        SuggestedRelevantFiles.Remove(relativePath);
    }

    [RelayCommand]
    private void DismissSuggestedRelevantFiles() => SuggestedRelevantFiles.Clear();

    // T14.1.2: a faster entry point into the same attachment mechanism AddContextFilesAsync already
    // uses (dedup + persist), not a new one - the relative path (what the mention popup shows and
    // what gets inserted into the prompt text) is resolved against WorkingDirectoryPath to get the
    // absolute path AttachedPaths actually stores.
    public void AttachMentionedFile(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(WorkingDirectoryPath, relativePath));
        AddAttachedPaths([fullPath]);
        _ = SaveAttachedPathsAsync();
        CloseMentionPopup();
    }

    [RelayCommand(CanExecute = nameof(CanRunPrompt))]
    private async Task RunPromptAsync()
    {
        if (!await _llmRunner.IsAvailableAsync())
        {
            // T6.3.2: ILlmRunner now has two implementations behind LlmRunnerSelector, so this can no
            // longer assume it's specifically the CLI that's missing - could just as easily be a
            // blank Anthropic API key.
            StatusMessage = "Prompt_LlmRunnerNotAvailable".Resolve();
            return;
        }

        IsRunning = true;
        CliOutput = string.Empty;
        StreamProgressDisplay = string.Empty;
        UsageSummary = string.Empty;
        ToolActivityLog.Clear();
        StatusMessage = "Prompt_Running".Resolve();

        _runCancellationTokenSource = new CancellationTokenSource();
        _timeoutCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        CancelPromptCommand.NotifyCanExecuteChanged();

        _runStopwatch = Stopwatch.StartNew();
        ElapsedTimeDisplay = "0s";
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => ElapsedTimeDisplay = $"{(int)_runStopwatch.Elapsed.TotalSeconds}s";
        _elapsedTimer.Start();

        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            _runCancellationTokenSource.Token, _timeoutCancellationTokenSource.Token);

        try
        {
            var fullPrompt = await BuildFullPromptAsync();

            // T6.2.1: fires on a background thread as the CLI streams assistant text, so UI state is
            // only ever touched after marshaling onto the UI thread. This used to re-parse the whole
            // growing CliOutput with the legacy markdown parser and push the result into
            // IPromptResultStore on every single token delta (T6.2.2's live file-reveal) - with
            // token-level streaming that's an O(n^2) regex re-scan plus a full file-list rebind
            // repeated hundreds/thousands of times per run, which froze the UI for the last, longest
            // responses. The user doesn't need to watch Claude's raw output stream in anyway, just see
            // that it's working, so this now only bumps a cheap O(1) character count; the authoritative
            // parse still happens once, below, via StructuredResponseParser against the complete output.
            void OnTextDelta(string text) => Dispatcher.UIThread.Post(() =>
            {
                CliOutput += text;
                StreamProgressDisplay = string.Format("Prompt_StreamProgress".Resolve(), CliOutput.Length);
            });

            // Epic 11 (F11.2.2): fires once per completed read-only tool call, on the same
            // background thread as OnTextDelta above - same UI-thread marshaling requirement.
            void OnToolActivity(string description) => Dispatcher.UIThread.Post(() => ToolActivityLog.Add(description));

            // T7.3.1: both this attempt and its retry below are the same logical turn, so they
            // resume from the same prior session (if any) rather than the retry starting its own.
            var resumeSessionId = ContinuePreviousConversation ? _lastSessionId : null;

            var result = await _llmRunner.RunPromptAsync(fullPrompt, WorkingDirectoryPath, OnTextDelta, OnToolActivity, resumeSessionId, linkedCancellationTokenSource.Token);
            StructuredParseResult? parseResult = null;

            if (result.Success)
            {
                parseResult = StructuredResponseParser.Parse(result.Output, WorkingDirectoryPath);

                // T5.1.2: one retry, feeding the schema validation error back to the model, before
                // settling for the markdown fallback (T5.1.3). A failed retry call (cancelled, CLI
                // error) keeps the original attempt's already-fallback-parsed result rather than
                // discarding it for an error state.
                if (parseResult.UsedFallback && parseResult.ValidationError is not null)
                {
                    var retryPrompt = fullPrompt + string.Format(RetryInstructionSuffix, parseResult.ValidationError);
                    var retryResult = await _llmRunner.RunPromptAsync(retryPrompt, WorkingDirectoryPath, OnTextDelta, OnToolActivity, resumeSessionId, linkedCancellationTokenSource.Token);

                    if (retryResult.Success)
                    {
                        result = CombineUsage(result, retryResult);
                        parseResult = StructuredResponseParser.Parse(retryResult.Output, WorkingDirectoryPath);
                    }
                }
            }

            // T7.3.1: captured regardless of ContinuePreviousConversation, so checking the box
            // right after this run (to continue it) has a session id ready to resume from.
            _lastSessionId = result.SessionId ?? _lastSessionId;
            HasPreviousSession = _lastSessionId is not null;

            CliOutput = result.Success || string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? result.Output
                : result.ErrorMessage;

            if (result.TotalCostUsd.HasValue)
            {
                UsageSummary = string.Format(
                    "Prompt_UsageSummary".Resolve(),
                    result.TotalCostUsd.Value,
                    result.InputTokens ?? 0,
                    result.OutputTokens ?? 0,
                    (result.DurationMs ?? 0) / 1000.0);
            }

            if (result.Success)
            {
                var parsed = parseResult!.Response;
                _promptResultStore.SetResult(parsed.Tutorial, parsed.Files, WorkingDirectoryPath, result.SessionId);

                var completionMessage = parsed.Files.Count > 0
                    ? string.Format("Prompt_RunCompletedWithFiles".Resolve(), parsed.Files.Count)
                    : "Prompt_RunCompleted".Resolve();

                StatusMessage = parseResult.UsedFallback
                    ? completionMessage + " " + "Prompt_FallbackParseNotice".Resolve()
                    : completionMessage;
            }
            else
            {
                StatusMessage = string.Format("Prompt_RunFailed".Resolve(), result.ExitCode);
            }

            // T7.1.1: persist this run (the prompt actually sent, its raw output, the parsed
            // files/selections currently in IPromptResultStore, and the apply log so far) now that
            // it has reached a terminal success/failure state - not for a cancelled run or one that
            // threw before producing a CLI result (caught below), since there's nothing meaningful
            // to persist for those. fullPrompt (not the retry prompt) and result.Output (the retry's
            // output, if a retry happened - CombineUsage already folded that in) are used so the
            // persisted record matches exactly what was actually sent and what actually got parsed.
            // PromptText (not fullPrompt) is also kept as RunSession.UserPromptText, for T7.2.3's
            // re-run/fork to reload into this same text box without it being sent through the
            // instruction template a second time.
            _runSessionStore.RecordRun(fullPrompt, result.Output, PromptText);
        }
        catch (OperationCanceledException)
        {
            // Both sources funnel into the same linked token, so the only way to tell a timeout
            // apart from a user-initiated cancel (T6.1.2) is which underlying source actually
            // requested it.
            StatusMessage = _timeoutCancellationTokenSource.IsCancellationRequested
                ? string.Format("Prompt_RunTimedOut".Resolve(), TimeoutSeconds)
                : "Prompt_RunCancelled".Resolve();
        }
        catch (Exception ex)
        {
            // Deliberately broad, not narrowed to specific exception types (T9.2.2): this wraps an
            // entire external CLI invocation plus JSON parsing of whatever it returned, and neither
            // of those has an enumerable closed set of exception types worth special-casing - a
            // user-facing status message and a log entry matter more here than which exception type
            // it happened to be.
            _logger.LogError(ex, "Prompt run failed.");
            StatusMessage = string.Format("Prompt_RunError".Resolve(), ex.Message);
        }
        finally
        {
            IsRunning = false;
            _runCancellationTokenSource?.Dispose();
            _runCancellationTokenSource = null;
            _timeoutCancellationTokenSource?.Dispose();
            _timeoutCancellationTokenSource = null;
            _elapsedTimer?.Stop();
            _elapsedTimer = null;
            _runStopwatch = null;
            CancelPromptCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelPrompt))]
    private void CancelPrompt() => _runCancellationTokenSource?.Cancel();

    // T5.1.2's retry is a second full LLM call, so cost/token/duration reporting (T6.2.3) has to add
    // both calls together rather than silently reporting only the retry's - otherwise a schema-failure
    // retry would under-report what the run actually spent.
    private static LlmRunResult CombineUsage(LlmRunResult original, LlmRunResult retry) => new()
    {
        Success = retry.Success,
        Output = retry.Output,
        ErrorMessage = retry.ErrorMessage,
        ExitCode = retry.ExitCode,
        TotalCostUsd = SumNullable(original.TotalCostUsd, retry.TotalCostUsd),
        DurationMs = SumNullable(original.DurationMs, retry.DurationMs),
        InputTokens = SumNullable(original.InputTokens, retry.InputTokens),
        OutputTokens = SumNullable(original.OutputTokens, retry.OutputTokens),
        SessionId = retry.SessionId ?? original.SessionId,
    };

    // T5.4.2: the exact string that would be sent to the LLM - shared by RunPromptAsync and the
    // live token estimate (UpdateEstimatedTokenCount) so the estimate can't drift from what a run
    // actually sends.
    private async Task<string> BuildFullPromptAsync()
    {
        // T5.3.2: read fresh on every call (not cached on OnNavigatedTo) so a change made on the
        // Settings page takes effect immediately - same rationale as ClaudeCliOptionsProvider
        // reading its settings fresh on every call.
        var instructionTemplateOverride = await _localSettingsService.ReadSettingAsync<string>(PromptTemplateSettingsKeys.Override);
        var instructionTemplate = string.IsNullOrWhiteSpace(instructionTemplateOverride)
            ? _promptTemplateProvider.GetTutorialInstructionPrompt()
            : instructionTemplateOverride;

        // T5.3.3: preset/depth guidance is additive to whatever the effective preamble is (default
        // or user-overridden) - it's extra instruction text, not schema, so it applies the same way
        // regardless of where the preamble came from.
        var presetInstruction = _promptTemplateProvider.GetPresetInstruction(SelectedPreset);
        var depthInstruction = _promptTemplateProvider.GetDepthInstruction(SelectedDepth);
        var extraInstructions = string.Concat(
            string.IsNullOrEmpty(presetInstruction) ? string.Empty : "\n\n" + presetInstruction,
            string.IsNullOrEmpty(depthInstruction) ? string.Empty : "\n\n" + depthInstruction);

        // T5.4.1: user-attached files/folders, embedded ahead of the user's own prompt text so the
        // model has their content before reading what to do with it.
        var contextFilesBlock = BuildContextFilesBlock(AttachedPaths);

        var fullPrompt = instructionTemplate + extraInstructions + contextFilesBlock + "\n\n" + PromptText;

        if (!string.IsNullOrWhiteSpace(WorkingDirectoryPath))
        {
            fullPrompt += $"\n\nThe working directory is: {WorkingDirectoryPath}";
        }

        return fullPrompt;
    }

    // T5.4.1: a folder attachment is expanded to the files directly inside it (non-recursive, see
    // AttachedPaths' own comment); a file attachment is used as-is. A file that can't be read as
    // text (binary, permissions, deleted since being attached) is skipped rather than failing the
    // whole run - folder attachment in particular can easily pick up a non-text file alongside the
    // ones that matter. Returns "" (no header) if nothing could be read.
    private static string BuildContextFilesBlock(IEnumerable<string> attachedPaths)
    {
        var filePaths = attachedPaths.SelectMany(path => Directory.Exists(path)
            ? Directory.EnumerateFiles(path)
            : File.Exists(path) ? [path] : Enumerable.Empty<string>());

        var block = new StringBuilder();

        foreach (var filePath in filePaths)
        {
            string content;

            try
            {
                content = File.ReadAllText(filePath);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            block.Append($"\n\n--- {filePath} ---\n{content}");
        }

        return block.Length > 0 ? "\n\nAdditional context files:" + block : string.Empty;
    }

    private static double? SumNullable(double? a, double? b) => a.HasValue || b.HasValue ? (a ?? 0) + (b ?? 0) : null;

    private static long? SumNullable(long? a, long? b) => a.HasValue || b.HasValue ? (a ?? 0) + (b ?? 0) : null;

    private static int? SumNullable(int? a, int? b) => a.HasValue || b.HasValue ? (a ?? 0) + (b ?? 0) : null;

    private bool CanRunPrompt() => !IsRunning && !string.IsNullOrWhiteSpace(PromptText);

    private bool CanCancelPrompt() => _runCancellationTokenSource is not null;

    partial void OnPromptTextChanged(string value)
    {
        RunPromptCommand.NotifyCanExecuteChanged();
        SuggestRelevantFilesCommand.NotifyCanExecuteChanged();
        UpdateEstimatedTokenCountDisplay();
    }

    partial void OnIsRunningChanged(bool value) => RunPromptCommand.NotifyCanExecuteChanged();

    partial void OnTimeoutSecondsChanged(int value) => _localSettingsService.SaveSettingAsync(TimeoutSecondsSettingsKey, value);

    partial void OnWorkingDirectoryPathChanged(string value)
    {
        // T7.3.1: a resumed CLI session is scoped to the directory it ran in, so switching
        // directories invalidates whatever conversation was in progress.
        _lastSessionId = null;
        HasPreviousSession = false;
        ContinuePreviousConversation = false;
        _cachedMentionFileTree = null;
        SuggestedRelevantFiles.Clear();
        SuggestRelevantFilesCommand.NotifyCanExecuteChanged();
        UpdateEstimatedTokenCountDisplay();

        // Epic 20 (specs/build-and-run): tells RunPanelViewModel's toolbar/output strip - a DI
        // singleton with no page-navigation lifecycle of its own - which working directory to
        // detect Run configurations for. Fires on every change, including the initial load from
        // settings in OnNavigatedTo below, which is also how the toolbar learns the saved working
        // directory on first launch without needing to visit this page.
        _workingDirectoryContext.SetCurrent(value);
    }

    partial void OnSelectedPresetChanged(PromptPreset value)
    {
        _localSettingsService.SaveSettingAsync(PresetSettingsKey, value.ToString());
        UpdateEstimatedTokenCountDisplay();
    }

    partial void OnSelectedDepthChanged(TutorialDepth value)
    {
        _localSettingsService.SaveSettingAsync(DepthSettingsKey, value.ToString());
        UpdateEstimatedTokenCountDisplay();
    }

    partial void OnSelectedAttachedPathChanged(string? value) => RemoveAttachedPathCommand.NotifyCanExecuteChanged();

    // T8.2.4: picking a recent working directory switches to it immediately and persists it the
    // same way BrowseWorkingDirectoryAsync does, then clears the selection back to null so the
    // dropdown returns to its placeholder rather than appearing permanently "stuck" on one entry.
    partial void OnSelectedRecentWorkingDirectoryChanged(string? value)
    {
        if (value is null)
        {
            return;
        }

        WorkingDirectoryPath = value;
        _localSettingsService.SaveSettingAsync(WorkingDirectorySettingsKeys.WorkingDirectory, WorkingDirectoryPath);
        SelectedRecentWorkingDirectory = null;
    }

    private async void UpdateEstimatedTokenCountDisplay()
    {
        var version = ++_tokenEstimateVersion;
        var fullPrompt = await BuildFullPromptAsync();

        if (version != _tokenEstimateVersion)
        {
            // A newer change (another keystroke, another attachment) started its own recompute
            // after this one - let that one's result win instead of overwriting it with ours.
            return;
        }

        EstimatedTokenCountDisplay = string.Format(
            "Prompt_EstimatedTokenCount".Resolve(), TokenEstimator.EstimateTokenCount(fullPrompt));
    }
}
