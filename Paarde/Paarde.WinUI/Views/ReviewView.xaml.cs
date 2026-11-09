using Microsoft.UI.Xaml.Controls;

using Paarde.Presentation.ViewModels;

namespace Paarde.WinUI.Views;

// Epic 26 (specs/interface-mode R6): FlowWizard mode's Review page - a thin wrapper that resolves the
// TutorialViewModel singleton (shared with the Assistant panel's Review tab) and hosts
// ReviewPanelControl, whose DataContext is supplied externally rather than resolved via DI.
public sealed partial class ReviewView : Page
{
    public TutorialViewModel ViewModel
    {
        get;
    }

    public ReviewView()
    {
        ViewModel = App.GetService<TutorialViewModel>();
        InitializeComponent();
    }
}
