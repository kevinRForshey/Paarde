using Avalonia.Threading;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Services;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Paarde.Avalonia.ViewModels;

// specs/ide-workbench-shell, R3: a DI singleton (constructed as a ShellViewModel property, same
// pattern as RunPanelViewModel) so it backs ShellView's status bar across every page navigation
// rather than being created and torn down with one page.
public partial class StatusBarViewModel : ObservableObject
{
    private readonly IGitStatusService _gitStatusService;
    private readonly ILanguageServerSessionManager _languageServerSessionManager;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger<StatusBarViewModel> _logger;

    private string _workingDirectoryPath = string.Empty;

    [ObservableProperty]
    private string? gitBranchName;

    [ObservableProperty]
    private int dirtyFileCount;

    [ObservableProperty]
    private string runConfigurationName = "(none)";

    [ObservableProperty]
    private string languageServerStatus = "LSP: none";

    // R3.5: null hides this slot entirely - there's no "Ln 0, Col 0" placeholder when no
    // CodeEditor has focus.
    [ObservableProperty]
    private string? caretPositionText;

    public StatusBarViewModel(
        IWorkingDirectoryContext workingDirectoryContext,
        IGitStatusService gitStatusService,
        ILanguageServerSessionManager languageServerSessionManager,
        RunPanelViewModel runPanelViewModel,
        IActiveEditorContext activeEditorContext,
        ILocalSettingsService localSettingsService,
        ILogger<StatusBarViewModel>? logger = null)
    {
        _gitStatusService = gitStatusService;
        _languageServerSessionManager = languageServerSessionManager;
        _localSettingsService = localSettingsService;
        _logger = logger ?? NullLogger<StatusBarViewModel>.Instance;

        workingDirectoryContext.Changed += OnWorkingDirectoryChanged;
        runPanelViewModel.PropertyChanged += OnRunPanelViewModelPropertyChanged;
        _languageServerSessionManager.DiagnosticsPublished += OnDiagnosticsPublished;
        _languageServerSessionManager.RunningLanguageIdsChanged += OnRunningLanguageIdsChanged;
        activeEditorContext.Changed += OnActiveEditorCaretPositionChanged;

        RunConfigurationName = runPanelViewModel.SelectedConfiguration?.Name ?? "(none)";

        // Covers the case where the user never visits the Prompt page this session - same reasoning
        // RunPanelViewModel.LoadSavedWorkingDirectoryAsync already documents for itself.
        _ = LoadSavedWorkingDirectoryAsync();
    }

    private async Task LoadSavedWorkingDirectoryAsync()
    {
        try
        {
            var savedWorkingDirectoryPath = await _localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory) ?? string.Empty;
            await SetWorkingDirectoryAsync(savedWorkingDirectoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the saved working directory for the status bar.");
        }
    }

    private void OnWorkingDirectoryChanged(string workingDirectoryPath) =>
        Dispatcher.UIThread.Post(() => _ = SetWorkingDirectoryAsync(workingDirectoryPath));

    private async Task SetWorkingDirectoryAsync(string workingDirectoryPath)
    {
        if (workingDirectoryPath == _workingDirectoryPath)
        {
            return;
        }

        _workingDirectoryPath = workingDirectoryPath;
        await RefreshGitStatusAsync();
    }

    private async Task RefreshGitStatusAsync()
    {
        if (string.IsNullOrWhiteSpace(_workingDirectoryPath))
        {
            GitBranchName = null;
            DirtyFileCount = 0;
            return;
        }

        // GetStatus shells out to the git CLI (synchronous, blocking I/O) - run off the UI thread
        // so a slow/unresponsive git never freezes the status bar.
        var workingDirectoryPath = _workingDirectoryPath;
        var status = await Task.Run(() => _gitStatusService.GetStatus(workingDirectoryPath));

        GitBranchName = status.BranchName;
        DirtyFileCount = status.DirtyFiles.Count;
    }

    private void OnRunPanelViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RunPanelViewModel.SelectedConfiguration) || sender is not RunPanelViewModel runPanelViewModel)
        {
            return;
        }

        RunConfigurationName = runPanelViewModel.SelectedConfiguration?.Name ?? "(none)";
    }

    // R3.4: re-read whenever a server publishes anything (evidence of its running state changing),
    // and whenever RunningLanguageIds' underlying set actually changes (StartAllAsync/StopAllAsync
    // completing) - the two together cover every state transition without this class needing to
    // know those two methods are the only things that change it.
    private void OnDiagnosticsPublished(string filePath, IReadOnlyList<LspDiagnostic> diagnostics) =>
        Dispatcher.UIThread.Post(RefreshLanguageServerStatus);

    private void OnRunningLanguageIdsChanged() =>
        Dispatcher.UIThread.Post(RefreshLanguageServerStatus);

    public void RefreshLanguageServerStatus()
    {
        var runningLanguageIds = _languageServerSessionManager.RunningLanguageIds;
        LanguageServerStatus = runningLanguageIds.Count == 0 ? "LSP: none" : $"LSP: {string.Join(", ", runningLanguageIds)}";
    }

    // Unlike OnDiagnosticsPublished above (a background LSP-read thread) or OnWorkingDirectoryChanged
    // (defensive - see its own remark), IActiveEditorContext.Changed is only ever raised from a
    // CodeEditor host's own Avalonia UI-thread event handlers (GotFocus/LostFocus/PropertyChanged),
    // so no Dispatcher marshal is needed here - and skipping it is what keeps this directly testable
    // in a plain xunit host, unlike the other three handlers (see StatusBarViewModelTests' remark).
    private void OnActiveEditorCaretPositionChanged(LspPosition? position) =>
        CaretPositionText = position is { } value ? $"Ln {value.Line + 1}, Col {value.Character + 1}" : null;
}
