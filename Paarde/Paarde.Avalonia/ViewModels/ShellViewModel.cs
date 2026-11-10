using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Views;

using CommunityToolkit.Mvvm.ComponentModel;

using FluentAvalonia.UI.Navigation;
using Paarde.Presentation.ViewModels;

namespace Paarde.Avalonia.ViewModels;

public partial class ShellViewModel : ObservableRecipient
{
    [ObservableProperty]
    private bool isBackEnabled;

    [ObservableProperty]
    private object? selected;

    public IFrameNavigationService NavigationService
    {
        get;
    }

    public INavigationViewService NavigationViewService
    {
        get;
    }

    // The persistent Build/Run/Stop toolbar
    // (design.md "Shell placement") - a singleton handed in here rather than created by this
    // ViewModel, so it's the same instance regardless of how many times ShellView/ShellViewModel
    // themselves get recreated.
    public RunPanelViewModel RunPanelViewModel
    {
        get;
    }

    // The status bar - same "singleton handed in, not created here"
    // shape as RunPanelViewModel above, for the same reason.
    public StatusBarViewModel StatusBarViewModel
    {
        get;
    }

    // The output dock (Build/Run/Problems/Terminal tabs) - same
    // "singleton handed in, not created here" shape as RunPanelViewModel/StatusBarViewModel above.
    public WorkbenchDockViewModel WorkbenchDockViewModel
    {
        get;
    }

    // The Quick Open popup - same "singleton handed in, not created here" shape
    // as RunPanelViewModel/StatusBarViewModel/WorkbenchDockViewModel above.
    public QuickOpenViewModel QuickOpenViewModel
    {
        get;
    }

    // The command palette popup - same "singleton handed in, not created here"
    // shape as the other ShellViewModel-owned singletons above.
    public CommandPaletteViewModel CommandPaletteViewModel
    {
        get;
    }

    // Go to Symbol in File popup - same "singleton handed in, not
    // created here" shape as the other ShellViewModel-owned singletons above.
    public SymbolQuickPickViewModel SymbolQuickPickViewModel
    {
        get;
    }

    public ShellViewModel(
        IFrameNavigationService navigationService,
        INavigationViewService navigationViewService,
        RunPanelViewModel runPanelViewModel,
        StatusBarViewModel statusBarViewModel,
        WorkbenchDockViewModel workbenchDockViewModel,
        QuickOpenViewModel quickOpenViewModel,
        CommandPaletteViewModel commandPaletteViewModel,
        SymbolQuickPickViewModel symbolQuickPickViewModel)
    {
        NavigationService = navigationService;
        NavigationService.Navigated += OnNavigated;
        NavigationViewService = navigationViewService;
        RunPanelViewModel = runPanelViewModel;
        StatusBarViewModel = statusBarViewModel;
        WorkbenchDockViewModel = workbenchDockViewModel;
        QuickOpenViewModel = quickOpenViewModel;
        CommandPaletteViewModel = commandPaletteViewModel;
        SymbolQuickPickViewModel = symbolQuickPickViewModel;

        // Wired here, not through DI constructor injection between the two
        // singletons, since that would be a circular dependency the container can't resolve.
        quickOpenViewModel.AttachCommandPalette(commandPaletteViewModel);
        commandPaletteViewModel.AttachQuickOpen(quickOpenViewModel);
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
