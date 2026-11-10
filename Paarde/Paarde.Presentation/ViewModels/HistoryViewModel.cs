using System.Collections.ObjectModel;

using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Contracts.ViewModels;
using Paarde.Presentation.Models;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paarde.Presentation.ViewModels;

// History page: lists every persisted RunSession, lets the user reopen one
// read-only via the same DiffsDetailControl the Diffs page uses, and re-run or fork its
// prompt back onto the Prompt page. Sessions/their Files are read fresh from
// IRunSessionStore on every navigation rather than cached, since a new run may have happened since
// this page was last visited.
public partial class HistoryViewModel : ObservableRecipient, INavigationAware
{
    private readonly IRunSessionStore _runSessionStore;
    private readonly INavigationService _navigationService;
    private readonly AssistantPanelViewModel _assistantPanel;

    [ObservableProperty]
    private HistorySessionRow? selected;

    [ObservableProperty]
    private ProposedFileChangeViewModel? selectedFile;

    public ObservableCollection<HistorySessionRow> Sessions { get; } = new();

    public ObservableCollection<ProposedFileChangeViewModel> SelectedFiles { get; } = new();

    public HistoryViewModel(IRunSessionStore runSessionStore, INavigationService navigationService, AssistantPanelViewModel assistantPanel)
    {
        _runSessionStore = runSessionStore;
        _navigationService = navigationService;
        _assistantPanel = assistantPanel;

        Sessions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoSessions));
        SelectedFiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectedHasNoFiles));
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(HasFilesButNoneSelected));
        };
    }

    public void OnNavigatedTo(object parameter)
    {
        Sessions.Clear();

        foreach (var session in _runSessionStore.ListSessions())
        {
            Sessions.Add(HistorySessionRow.From(session));
        }

        Selected = Sessions.FirstOrDefault();
    }

    public void OnNavigatedFrom()
    {
    }

    // Drives the History page's empty-state messaging, same rationale as DiffsViewModel.HasNoFiles.
    public bool HasNoSessions => Sessions.Count == 0;

    // The selected run produced no files (e.g. it was answered as a plain question, not a change
    // request) - distinct from no session being selected at all.
    public bool SelectedHasNoFiles => Selected is not null && SelectedFiles.Count == 0;

    public bool HasFiles => SelectedFiles.Count > 0;

    public bool HasFilesButNoneSelected => SelectedFiles.Count > 0 && SelectedFile is null;

    partial void OnSelectedChanged(HistorySessionRow? value)
    {
        SelectedFiles.Clear();
        SelectedFile = null;

        if (value is not null)
        {
            foreach (var file in value.Session.Files.Select(RunSessionFileConverter.ToProposedFileChangeViewModel))
            {
                SelectedFiles.Add(file);
            }

            SelectedFile = SelectedFiles.FirstOrDefault();
        }

        OnPropertyChanged(nameof(SelectedHasNoFiles));
        OnPropertyChanged(nameof(HasFiles));
        ForkCommand.NotifyCanExecuteChanged();
        RerunCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private void Fork() => Launch(autoRun: false);

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private void Rerun() => Launch(autoRun: true);

    private bool CanLaunch() => Selected is not null;

    private void Launch(bool autoRun)
    {
        if (Selected is not { } session)
        {
            return;
        }

        _assistantPanel.Compose.LoadLaunchRequest(new PromptLaunchRequest(session.Session.UserPromptText, session.Session.WorkingDirectoryPath, autoRun));
        _assistantPanel.ShowCompose();
        _navigationService.NavigateTo(typeof(ExplorerViewModel).FullName!);
    }
}
