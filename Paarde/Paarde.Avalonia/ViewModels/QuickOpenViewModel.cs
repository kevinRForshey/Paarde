using System.Collections.ObjectModel;

using Avalonia.Threading;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.Strings;
using Paarde.Core.Helpers;
using Paarde.Core.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Paarde.Avalonia.ViewModels;

// Epic 24, R1: a DI singleton (ShellViewModel property, same "singleton handed in" shape as
// RunPanelViewModel/StatusBarViewModel) so Ctrl+P reaches the same instance regardless of which
// page is currently navigated to. Keeps its own FileTreeEntry cache rather than sharing
// PromptViewModel's @mention cache - same duplication Epic 14 already accepts between its own
// call sites, not worth a shared service for two consumers.
public partial class QuickOpenViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger<QuickOpenViewModel> _logger;

    private string _workingDirectoryPath = string.Empty;
    private FileTreeEntry? _cachedTree;
    private string? _cachedTreeWorkingDirectory;

    // Epic 24, R3.2: set once by ShellViewModel after both singletons exist, so opening this
    // popup can close the command palette without the two depending on each other through DI
    // construction (which would be a circular singleton dependency).
    private CommandPaletteViewModel? _commandPaletteViewModel;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string query = string.Empty;

    [ObservableProperty]
    private string? selectedResult;

    [ObservableProperty]
    private string? emptyStateMessage;

    public ObservableCollection<string> Results { get; } = new();

    public QuickOpenViewModel(
        INavigationService navigationService,
        ILocalSettingsService localSettingsService,
        IWorkingDirectoryContext workingDirectoryContext,
        ILogger<QuickOpenViewModel>? logger = null)
    {
        _navigationService = navigationService;
        _localSettingsService = localSettingsService;
        _logger = logger ?? NullLogger<QuickOpenViewModel>.Instance;

        workingDirectoryContext.Changed += OnWorkingDirectoryChanged;

        // Covers the case where the user never visits the Prompt page this session - same
        // reasoning StatusBarViewModel.LoadSavedWorkingDirectoryAsync already documents.
        _ = LoadSavedWorkingDirectoryAsync();
    }

    public void AttachCommandPalette(CommandPaletteViewModel commandPaletteViewModel) =>
        _commandPaletteViewModel = commandPaletteViewModel;

    private async Task LoadSavedWorkingDirectoryAsync()
    {
        try
        {
            _workingDirectoryPath = await _localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory) ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the saved working directory for Quick Open.");
        }
    }

    private void OnWorkingDirectoryChanged(string workingDirectoryPath) =>
        Dispatcher.UIThread.Post(() => _workingDirectoryPath = workingDirectoryPath);

    [RelayCommand]
    private void Open()
    {
        _commandPaletteViewModel?.CloseCommand.Execute(null);
        Query = string.Empty;
        Refilter(Query);
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void ActivateSelected()
    {
        if (SelectedResult is not { } selectedResult)
        {
            return;
        }

        var absolutePath = Path.Combine(_workingDirectoryPath, selectedResult);
        _navigationService.NavigateTo(typeof(ExplorerViewModel).FullName!, absolutePath);
        IsOpen = false;
    }

    partial void OnQueryChanged(string value) => Refilter(value);

    private void Refilter(string query)
    {
        Results.Clear();
        SelectedResult = null;

        if (string.IsNullOrWhiteSpace(_workingDirectoryPath) || !Directory.Exists(_workingDirectoryPath))
        {
            EmptyStateMessage = "QuickOpen_NoWorkingDirectory".Resolve();
            return;
        }

        EmptyStateMessage = null;

        if (_cachedTree is null || _cachedTreeWorkingDirectory != _workingDirectoryPath)
        {
            _cachedTree = FileTreeBuilder.Build(_workingDirectoryPath);
            _cachedTreeWorkingDirectory = _workingDirectoryPath;
        }

        foreach (var match in FileMentionMatcher.FindMatches(_cachedTree, query))
        {
            Results.Add(match);
        }

        SelectedResult = Results.FirstOrDefault();
    }
}
