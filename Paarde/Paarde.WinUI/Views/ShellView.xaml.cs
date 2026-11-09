using Microsoft.UI.Xaml.Controls;

using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Models;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;
using Paarde.WinUI.Helpers;
using Paarde.WinUI.ViewModels;

namespace Paarde.WinUI.Views;

public sealed partial class ShellView : UserControl
{
    public ShellViewModel ViewModel
    {
        get;
    }

    public ShellView()
    {
        ViewModel = App.GetService<ShellViewModel>();
        InitializeComponent();

        ViewModel.NavigationService.Frame = NavigationFrame;
        ViewModel.NavigationViewService.Initialize(NavigationViewControl);

        InsertFlowWizardMenuItemsIfNeededAsync();
    }

    // NavigationView.SelectedItem is the raw NavigationViewItem; the header shows that item's label.
    // Instance (not static) because x:Bind's generated code calls it through the page instance.
    public object? GetHeader(object? selected) => (selected as NavigationViewItem)?.Content;

    // Epic 26 (specs/interface-mode R3): FlowWizard mode gets Home/Compose/Review menu items ahead
    // of EditorFocused mode's Explorer/Terminal/History - inserted here rather than in XAML since
    // which set applies is a runtime setting. Fire-and-forget like Paarde.Avalonia's: the only effect
    // of this resolving after the first navigation is that navigation's selected-item highlight,
    // which self-corrects on the next one.
    private async void InsertFlowWizardMenuItemsIfNeededAsync()
    {
        var localSettingsService = App.GetService<ILocalSettingsService>();
        var interfaceMode = await localSettingsService.ReadSettingAsync<InterfaceMode?>(InterfaceModeSettingsKeys.Mode) ?? InterfaceMode.EditorFocused;
        if (interfaceMode != InterfaceMode.FlowWizard)
        {
            return;
        }

        var homeItem = new NavigationViewItem { Content = "Home", Icon = new SymbolIcon(Symbol.Home) };
        NavigationHelper.SetNavigateTo(homeItem, typeof(MainViewModel).FullName);

        var composeItem = new NavigationViewItem { Content = "Compose", Icon = new SymbolIcon(Symbol.Send) };
        NavigationHelper.SetNavigateTo(composeItem, typeof(PromptViewModel).FullName);

        var reviewItem = new NavigationViewItem { Content = "Review", Icon = new SymbolIcon(Symbol.Document) };
        NavigationHelper.SetNavigateTo(reviewItem, typeof(TutorialViewModel).FullName);

        NavigationViewControl.MenuItems.Insert(0, reviewItem);
        NavigationViewControl.MenuItems.Insert(0, composeItem);
        NavigationViewControl.MenuItems.Insert(0, homeItem);
    }
}
