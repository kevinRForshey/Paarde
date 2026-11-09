using Microsoft.UI.Xaml.Controls;

using Paarde.Presentation.ViewModels;

namespace Paarde.WinUI.Views;

public sealed partial class PromptView : Page
{
    public PromptViewModel ViewModel
    {
        get;
    }

    public PromptView()
    {
        ViewModel = App.GetService<PromptViewModel>();
        InitializeComponent();
    }
}
