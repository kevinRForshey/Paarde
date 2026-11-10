using System.Collections.ObjectModel;

using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Contracts.ViewModels;
using Paarde.Presentation.Models;
using Paarde.Presentation.Services;
using Paarde.Core.Contracts.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paarde.Presentation.ViewModels;

// The FlowWizard shell's landing page - recent runs and recent
// projects pulled from the same IRunSessionStore the History page already reads, plus a
// quick-prompt entry point that hands off to the Compose page (PromptViewModel) rather than
// duplicating its folder-picking/CLI-invocation logic here. Takes PromptViewModel directly
// (instead of routing through a nav parameter) because PromptViewModel no longer implements
// INavigationAware (removed by Epic 25) - the same direct-call pattern HistoryViewModel.Launch
// already uses for Fork/Rerun.
public partial class MainViewModel : ObservableRecipient, INavigationAware
{
    // Keeps the dashboard to a glance-able size rather than reproducing the full History page here.
    private const int MaxRecentRuns = 5;
    private const int MaxRecentProjects = 5;

    private readonly IRunSessionStore _runSessionStore;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly INavigationService _navigationService;
    private readonly PromptViewModel _promptViewModel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendQuickPromptCommand))]
    private string quickPromptText = string.Empty;

    // Selection-driven, mirroring HistoryViewModel.Selected/ForkCommand/RerunCommand, rather than a
    // command-per-row reaching back out to this view model's DataContext from inside an
    // ItemsControl's DataTemplate.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenRecentRunCommand))]
    private HistorySessionRow? selectedRecentRun;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenRecentProjectCommand))]
    private string? selectedRecentProject;

    public ObservableCollection<HistorySessionRow> RecentRuns { get; } = new();

    public ObservableCollection<string> RecentProjects { get; } = new();

    public MainViewModel(IRunSessionStore runSessionStore, ILocalSettingsService localSettingsService, INavigationService navigationService, PromptViewModel promptViewModel)
    {
        _runSessionStore = runSessionStore;
        _localSettingsService = localSettingsService;
        _navigationService = navigationService;
        _promptViewModel = promptViewModel;

        RecentRuns.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentRuns));
        RecentProjects.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentProjects));
    }

    public void OnNavigatedTo(object parameter)
    {
        RecentRuns.Clear();
        RecentProjects.Clear();
        SelectedRecentRun = null;
        SelectedRecentProject = null;

        // Newest-first (IRunSessionStore.ListSessions' own contract), so both Take(MaxRecentRuns)
        // and the Distinct() below (which keeps the first - i.e. most recent - occurrence of each
        // path) land on the right rows without re-sorting here.
        var sessions = _runSessionStore.ListSessions();

        foreach (var session in sessions.Take(MaxRecentRuns))
        {
            RecentRuns.Add(HistorySessionRow.From(session));
        }

        foreach (var workingDirectoryPath in sessions
            .Select(session => session.WorkingDirectoryPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct()
            .Take(MaxRecentProjects))
        {
            RecentProjects.Add(workingDirectoryPath);
        }
    }

    public void OnNavigatedFrom()
    {
    }

    public bool HasRecentRuns => RecentRuns.Count > 0;

    public bool HasRecentProjects => RecentProjects.Count > 0;

    // Reopens a past run on the Compose page for editing (same semantics as HistoryViewModel.Fork) -
    // not an immediate re-run, since landing on this page is the app's first screen and sending a
    // prompt to the CLI without a chance to review it first would be surprising here.
    [RelayCommand(CanExecute = nameof(CanOpenRecentRun))]
    private void OpenRecentRun()
    {
        if (SelectedRecentRun is not { } row)
        {
            return;
        }

        _promptViewModel.LoadLaunchRequest(new PromptLaunchRequest(row.Session.UserPromptText, row.Session.WorkingDirectoryPath, AutoRun: false));
        _navigationService.NavigateTo(typeof(PromptViewModel).FullName!);
    }

    private bool CanOpenRecentRun() => SelectedRecentRun is not null;

    // Fast project switching (this page's dashboard miniature of HistoryViewModel's own working-
    // directory handling): persists the chosen path as the saved working directory - the same
    // setting PromptViewModel/ExplorerViewModel already read - and also sets it directly on
    // PromptViewModel (a singleton that only reads the saved setting once, at construction, so
    // saving alone wouldn't be picked up by the already-running instance), rather than clearing
    // whatever draft prompt text is already there the way a PromptLaunchRequest would.
    [RelayCommand(CanExecute = nameof(CanOpenRecentProject))]
    private async Task OpenRecentProjectAsync()
    {
        if (SelectedRecentProject is not { } workingDirectoryPath)
        {
            return;
        }

        await _localSettingsService.SaveSettingAsync(WorkingDirectorySettingsKeys.WorkingDirectory, workingDirectoryPath);
        _promptViewModel.WorkingDirectoryPath = workingDirectoryPath;
        _navigationService.NavigateTo(typeof(PromptViewModel).FullName!);
    }

    private bool CanOpenRecentProject() => SelectedRecentProject is not null;

    // Loads the last-used working directory (if any) alongside the typed-here text, but doesn't
    // auto-run - the user hasn't had a chance to review the prompt or pick a different folder yet,
    // same reasoning as OpenRecentRun above.
    [RelayCommand(CanExecute = nameof(CanSendQuickPrompt))]
    private async Task SendQuickPromptAsync()
    {
        var workingDirectoryPath = await _localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory) ?? string.Empty;

        _promptViewModel.LoadLaunchRequest(new PromptLaunchRequest(QuickPromptText, workingDirectoryPath, AutoRun: false));
        _navigationService.NavigateTo(typeof(PromptViewModel).FullName!);
    }

    private bool CanSendQuickPrompt() => !string.IsNullOrWhiteSpace(QuickPromptText);
}
