using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Views;

using CommunityToolkit.Mvvm.ComponentModel;

using FluentAvalonia.UI.Navigation;

namespace Paarde.Avalonia.ViewModels;

public partial class ShellViewModel : ObservableRecipient
{
    [ObservableProperty]
    private bool isBackEnabled;

    [ObservableProperty]
    private object? selected;

    public INavigationService NavigationService
    {
        get;
    }

    public INavigationViewService NavigationViewService
    {
        get;
    }

    // Epic 20 (specs/build-and-run F20.3): the persistent Build/Run/Stop toolbar
    // (design.md "Shell placement") - a singleton handed in here rather than created by this
    // ViewModel, so it's the same instance regardless of how many times ShellView/ShellViewModel
    // themselves get recreated.
    public RunPanelViewModel RunPanelViewModel
    {
        get;
    }

    // specs/ide-workbench-shell, R3: the status bar - same "singleton handed in, not created here"
    // shape as RunPanelViewModel above, for the same reason.
    public StatusBarViewModel StatusBarViewModel
    {
        get;
    }

    // specs/ide-workbench-shell, R1: the output dock (Build/Run/Problems/Terminal tabs) - same
    // "singleton handed in, not created here" shape as RunPanelViewModel/StatusBarViewModel above.
    public WorkbenchDockViewModel WorkbenchDockViewModel
    {
        get;
    }

    public ShellViewModel(
        INavigationService navigationService,
        INavigationViewService navigationViewService,
        RunPanelViewModel runPanelViewModel,
        StatusBarViewModel statusBarViewModel,
        WorkbenchDockViewModel workbenchDockViewModel)
    {
        NavigationService = navigationService;
        NavigationService.Navigated += OnNavigated;
        NavigationViewService = navigationViewService;
        RunPanelViewModel = runPanelViewModel;
        StatusBarViewModel = statusBarViewModel;
        WorkbenchDockViewModel = workbenchDockViewModel;
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        IsBackEnabled = NavigationService.CanGoBack;

        if (e.SourcePageType == typeof(SettingsView))
        {
            Selected = NavigationViewService.SettingsItem;
            return;
        }

        var selectedItem = NavigationViewService.GetSelectedItem(e.SourcePageType);
        if (selectedItem != null)
        {
            Selected = selectedItem;
        }
    }
}
