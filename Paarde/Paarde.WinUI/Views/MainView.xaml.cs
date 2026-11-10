using Microsoft.UI.Xaml.Controls;

using Paarde.Presentation.ViewModels;

namespace Paarde.WinUI.Views;

public sealed partial class MainView : Page
{
    public MainViewModel ViewModel
    {
        get;
    }

    public MainView()
    {
        ViewModel = App.GetService<MainViewModel>();
        InitializeComponent();
    }
}
