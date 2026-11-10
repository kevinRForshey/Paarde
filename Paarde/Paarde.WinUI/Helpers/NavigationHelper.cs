using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Paarde.WinUI.Helpers;

// Helper class to set the navigation target for a NavigationViewItem.
//
// Usage in XAML:
// <NavigationViewItem Content="Explorer" helpers:NavigationHelper.NavigateTo="Paarde.Presentation.ViewModels.ExplorerViewModel" />
//
// Usage in code:
// NavigationHelper.SetNavigateTo(navigationViewItem, typeof(MainViewModel).FullName);
public static class NavigationHelper
{
    public static readonly DependencyProperty NavigateToProperty =
        DependencyProperty.RegisterAttached("NavigateTo", typeof(string), typeof(NavigationHelper), new PropertyMetadata(null));

    public static string? GetNavigateTo(NavigationViewItem item) => (string?)item.GetValue(NavigateToProperty);

    public static void SetNavigateTo(NavigationViewItem item, string? value) => item.SetValue(NavigateToProperty, value);
}
