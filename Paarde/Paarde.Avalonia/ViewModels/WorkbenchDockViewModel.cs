using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Services;
using Paarde.Core.Contracts.Services;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Paarde.Avalonia.ViewModels;

// specs/ide-workbench-shell, R1: a DI singleton (constructed as a ShellViewModel property, same
// pattern as RunPanelViewModel/StatusBarViewModel) so it backs ShellView's output dock across
// every page navigation instead of being created and torn down with one page.
public partial class WorkbenchDockViewModel : ObservableObject
{
    private readonly ILocalSettingsService _localSettingsService;

    public RunPanelViewModel RunPanelViewModel { get; }

    // specs/ide-workbench-shell R1.3: promoted to a DI singleton (App.axaml.cs) so this is the
    // exact same instance TerminalView (the page) binds to - one live PTY session, not two.
    public TerminalViewModel TerminalViewModel { get; }

    public IDiagnosticsAggregator DiagnosticsAggregator { get; }

    // "Build" | "Run" | "Problems" | "Terminal" - a string rather than an enum/index because it's
    // what persists via ILocalSettingsService and what ShellView.axaml.cs's TabControl-index
    // lookup keys off of.
    [ObservableProperty]
    private string activeTabKey = "Run";

    [ObservableProperty]
    private bool isExpanded;

    public WorkbenchDockViewModel(
        RunPanelViewModel runPanelViewModel,
        TerminalViewModel terminalViewModel,
        IDiagnosticsAggregator diagnosticsAggregator,
        ILocalSettingsService localSettingsService)
    {
        RunPanelViewModel = runPanelViewModel;
        TerminalViewModel = terminalViewModel;
        DiagnosticsAggregator = diagnosticsAggregator;
        _localSettingsService = localSettingsService;

        runPanelViewModel.PropertyChanged += OnRunPanelViewModelPropertyChanged;

        _ = RestoreAsync();
    }

    private async Task RestoreAsync()
    {
        ActiveTabKey = await _localSettingsService.ReadSettingAsync<string>(WorkbenchDockSettingsKeys.ActiveTab) ?? "Run";
        IsExpanded = await _localSettingsService.ReadSettingAsync<bool?>(WorkbenchDockSettingsKeys.IsExpanded) ?? false;
    }

    partial void OnActiveTabKeyChanged(string value) =>
        _ = _localSettingsService.SaveSettingAsync(WorkbenchDockSettingsKeys.ActiveTab, value);

    partial void OnIsExpandedChanged(bool value) =>
        _ = _localSettingsService.SaveSettingAsync(WorkbenchDockSettingsKeys.IsExpanded, value);

    // R5.2-equivalent (specs/build-and-run): starting an action should bring you to it - the old
    // RunPanelViewModel.IsOutputExpanded auto-expand-on-build-or-run behavior, now expressed as
    // "expand the dock and switch to the tab for whatever just started" instead of "expand the
    // one strip" since there's no longer just one strip.
    private void OnRunPanelViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RunPanelViewModel.IsBuilding) && RunPanelViewModel.IsBuilding)
        {
            IsExpanded = true;
            ActiveTabKey = "Build";
        }
        else if (e.PropertyName == nameof(RunPanelViewModel.IsRunning) && RunPanelViewModel.IsRunning)
        {
            IsExpanded = true;
            ActiveTabKey = "Run";
        }
    }
}
