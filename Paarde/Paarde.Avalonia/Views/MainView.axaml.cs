using Avalonia.Controls;

using Paarde.Avalonia.ViewModels;

namespace Paarde.Avalonia.Views;

public partial class MainView : UserControl
{
    public MainViewModel ViewModel
    {
        get;
    }

    public MainView()
    {
        ViewModel = App.GetService<MainViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }
}
