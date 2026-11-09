using Avalonia.Controls;

using Paarde.Presentation.ViewModels;

namespace Paarde.Avalonia.Views;

public partial class SettingsView : UserControl
{
    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsView()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }
}
