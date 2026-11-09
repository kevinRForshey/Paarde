using Avalonia.Controls;

using Paarde.Presentation.ViewModels;

namespace Paarde.Avalonia.Views;

// Epic 26 (specs/interface-mode R6): FlowWizard mode's Review page - a thin wrapper that resolves
// the TutorialViewModel singleton (the same one AssistantPanel.Review already shares with
// EditorFocused mode's docked panel) and hosts ReviewPanelControl, whose DataContext is supplied
// externally rather than resolved via DI (see that control's own remark).
public partial class ReviewView : UserControl
{
    public TutorialViewModel ViewModel
    {
        get;
    }

    public ReviewView()
    {
        ViewModel = App.GetService<TutorialViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }
}
