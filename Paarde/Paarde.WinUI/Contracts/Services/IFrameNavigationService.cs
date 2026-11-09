using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

using Paarde.Presentation.Contracts.Services;

namespace Paarde.WinUI.Contracts.Services;

// The WinUI-only face of NavigationService: the Frame it drives and its raw Navigated event, which
// only ShellView/ShellViewModel need. View models depend on Presentation's INavigationService instead
// (mirrors Paarde.Avalonia's IFrameNavigationService).
public interface IFrameNavigationService : INavigationService
{
    event NavigatedEventHandler Navigated;

    Frame? Frame
    {
        get; set;
    }
}
