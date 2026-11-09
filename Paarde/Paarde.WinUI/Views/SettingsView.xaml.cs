using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Paarde.Core.Models;
using Paarde.Presentation.Models;
using Paarde.Presentation.ViewModels;

namespace Paarde.WinUI.Views;

public sealed partial class SettingsView : Page
{
    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsView()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e) =>
        ViewModel.SwitchThemeCommand.Execute(ParseTag<AppTheme>(sender));

    private void OnInterfaceModeChecked(object sender, RoutedEventArgs e) =>
        ViewModel.InterfaceMode = ParseTag<InterfaceMode>(sender);

    private void OnRunnerKindChecked(object sender, RoutedEventArgs e) =>
        ViewModel.SelectedRunnerKind = ParseTag<LlmRunnerKind>(sender);

    private static T ParseTag<T>(object sender)
        where T : struct, Enum => Enum.Parse<T>((string)((FrameworkElement)sender).Tag);
}
