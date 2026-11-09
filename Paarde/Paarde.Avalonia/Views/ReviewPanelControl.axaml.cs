using Avalonia.Controls;

using Paarde.Avalonia.Helpers;
using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Services;

namespace Paarde.Avalonia.Views;

public partial class ReviewPanelControl : UserControl
{
    // Epic 25, T11: unlike TutorialView (a navigated page that resolved its own TutorialViewModel
    // from DI), this control's DataContext is supplied externally - AssistantPanelControl binds it
    // to AssistantPanel.Review - so there's no ViewModel property/DataContext assignment here; every
    // binding in the XAML resolves against whatever DataContext is handed to this control.
    private readonly ResizablePaneHelper _paneHelper;

    public ReviewPanelControl()
    {
        InitializeComponent();

        _paneHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[0], App.GetService<ILocalSettingsService>(), WorkspaceLayoutSettingsKeys.ReviewFileListWidth, defaultWidth: 280);
        Loaded += async (_, _) => await _paneHelper.RestoreAsync();
        FileListSplitter.DragCompleted += (_, _) => _paneHelper.OnSplitterDragCompleted();
    }
}
