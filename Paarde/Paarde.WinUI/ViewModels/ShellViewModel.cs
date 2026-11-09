using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml.Navigation;

using Paarde.Presentation.ViewModels;
using Paarde.WinUI.Contracts.Services;
using Paarde.WinUI.Views;

namespace Paarde.WinUI.ViewModels;

// WinUI counterpart of Paarde.Avalonia's ShellViewModel: keeps NavigationView's selection and back
// button in sync with the Frame, and owns the shell-level singletons. Each shell's nav sync is
// framework-specific, so this deliberately isn't shared (specs/winui-frontend, out of scope).
public partial class ShellViewModel : ObservableRecipient
{
    // Partial properties rather than Paarde.Avalonia's [ObservableProperty] fields: in a WinUI project
    // the toolkit flags fields (MVVMTK0045) since only properties get CsWinRT's marshalling code.
    [ObservableProperty]
    public partial bool IsBackEnabled { get; set; }

    [ObservableProperty]
    public partial object? Selected { get; set; }

    public IFrameNavigationService NavigationService
    {
        get;
    }

    public INavigationViewService NavigationViewService
    {
        get;
    }

    // Epic 20 (specs/build-and-run F20.3): the persistent Build/Run/Stop toolbar - a singleton
    // handed in here rather than created by this ViewModel, so it's the same instance regardless of
    // how many times ShellView/ShellViewModel themselves get recreated.
    public RunPanelViewModel RunPanelViewModel
    {
        get;
    }

    // specs/ide-workbench-shell, R3: the status bar - same "singleton handed in" shape.
    public StatusBarViewModel StatusBarViewModel
    {
        get;
    }

    // specs/ide-workbench-shell, R1: the output dock - same "singleton handed in" shape. Taking it
    // here is also what constructs it (and so DiagnosticsAggregator's subscription) before any page
    // navigates.
    public WorkbenchDockViewModel WorkbenchDockViewModel
    {
        get;
    }

    // Epic 24, R1/R3: the Quick Open popup.
    public QuickOpenViewModel QuickOpenViewModel
    {
        get;
    }

    // Epic 24, R2/R3: the command palette popup.
    public CommandPaletteViewModel CommandPaletteViewModel
    {
        get;
    }

    // specs/editor-intelligence R7: Go to Symbol in File popup.
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

        // Epic 24, R3.2: wired here, not through DI constructor injection between the two
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
