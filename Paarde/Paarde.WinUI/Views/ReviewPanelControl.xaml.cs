using Microsoft.UI.Xaml.Controls;

using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;
using Paarde.WinUI.Helpers;

namespace Paarde.WinUI.Views;

public sealed partial class ReviewPanelControl : UserControl
{
    // Epic 25, T11: this control's DataContext is supplied externally (ReviewView, or the Assistant
    // panel's Review tab), so ViewModel just mirrors it for x:Bind rather than resolving from DI.
    public TutorialViewModel? ViewModel { get; private set; }

    private readonly ResizablePaneHelper _paneHelper;

    public ReviewPanelControl()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (!ReferenceEquals(ViewModel, DataContext))
            {
                ViewModel = DataContext as TutorialViewModel;
                Bindings.Update();
            }
        };

        _paneHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[0], App.GetService<ILocalSettingsService>(), WorkspaceLayoutSettingsKeys.ReviewFileListWidth, defaultWidth: 280);
        _paneHelper.AttachSplitter(FileListSplitter);
        Loaded += async (_, _) => await _paneHelper.RestoreAsync();
    }
}
