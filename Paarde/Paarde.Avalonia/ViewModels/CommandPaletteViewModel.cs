using System.Collections.ObjectModel;

using Paarde.Avalonia.Contracts.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paarde.Avalonia.ViewModels;

// Epic 24, R2.2: a named, filterable command. CanExecute/Execute are plain delegates rather than
// ICommand so Build/Run/Stop (delegating to RunPanelViewModel's existing IAsyncRelayCommand/
// IRelayCommand) and "Go to <Page>" (a plain NavigateTo call) can share one shape without either
// kind needing a CommandParameter.
public sealed record PaletteCommand(string Name, Func<bool> CanExecute, Action Execute)
{
    // R2.4: a snapshot taken when this entry is added to Results, not a live binding - the same
    // "re-evaluated on refresh, not pushed" shape every other list in this app already uses, so
    // the command palette's disabled state doesn't need its own change-notification plumbing.
    public bool IsEnabled => CanExecute();
}

// Epic 24, R2: a DI singleton (ShellViewModel property, same shape as QuickOpenViewModel) so
// Ctrl+Shift+P reaches the same instance regardless of which page is currently navigated to. The
// registry is a hand-written list, not a reflection-based scan of every [RelayCommand] in the app
// - see requirements.md's non-goals.
public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly IReadOnlyList<PaletteCommand> _registry;

    // Epic 24, R3.2: set once by ShellViewModel after both singletons exist - see
    // QuickOpenViewModel's matching field for why this isn't constructor injection.
    private QuickOpenViewModel? _quickOpenViewModel;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string query = string.Empty;

    [ObservableProperty]
    private PaletteCommand? selectedResult;

    public ObservableCollection<PaletteCommand> Results { get; } = new();

    public CommandPaletteViewModel(RunPanelViewModel runPanelViewModel, INavigationService navigationService)
    {
        PaletteCommand GoTo(string name, Type viewModelType) =>
            new($"Go to {name}", () => true, () => navigationService.NavigateTo(viewModelType.FullName!));

        _registry = new List<PaletteCommand>
        {
            new("Build", () => runPanelViewModel.BuildCommand.CanExecute(null), () => runPanelViewModel.BuildCommand.Execute(null)),
            new("Run", () => runPanelViewModel.RunCommand.CanExecute(null), () => runPanelViewModel.RunCommand.Execute(null)),
            new("Stop", () => runPanelViewModel.StopCommand.CanExecute(null), () => runPanelViewModel.StopCommand.Execute(null)),
            GoTo("Main", typeof(MainViewModel)),
            GoTo("Prompt", typeof(PromptViewModel)),
            GoTo("Explorer", typeof(ExplorerViewModel)),
            GoTo("Terminal", typeof(TerminalViewModel)),
            GoTo("Diffs", typeof(DiffsViewModel)),
            GoTo("Tutorial", typeof(TutorialViewModel)),
            GoTo("History", typeof(HistoryViewModel)),
            GoTo("Settings", typeof(SettingsViewModel)),
        };
    }

    public void AttachQuickOpen(QuickOpenViewModel quickOpenViewModel) => _quickOpenViewModel = quickOpenViewModel;

    [RelayCommand]
    private void Open()
    {
        _quickOpenViewModel?.CloseCommand.Execute(null);
        Query = string.Empty;
        Refilter(Query);
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void ExecuteSelected()
    {
        if (SelectedResult is { } selected && selected.CanExecute())
        {
            selected.Execute();
        }

        IsOpen = false;
    }

    partial void OnQueryChanged(string value) => Refilter(value);

    private void Refilter(string query)
    {
        Results.Clear();

        var matches = string.IsNullOrWhiteSpace(query)
            ? _registry
            : _registry.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var match in matches)
        {
            Results.Add(match);
        }

        SelectedResult = Results.FirstOrDefault();
    }
}
