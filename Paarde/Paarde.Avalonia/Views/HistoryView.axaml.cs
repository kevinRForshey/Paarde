using Avalonia.Controls;

using Paarde.Avalonia.Helpers;
using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;

namespace Paarde.Avalonia.Views;

public partial class HistoryView : UserControl
{
    public HistoryViewModel ViewModel
    {
        get;
    }

    private readonly ResizablePaneHelper _sessionListPaneHelper;
    private readonly ResizablePaneHelper _fileListPaneHelper;

    public HistoryView()
    {
        ViewModel = App.GetService<HistoryViewModel>();
        DataContext = ViewModel;
        InitializeComponent();

        var localSettingsService = App.GetService<ILocalSettingsService>();
        _sessionListPaneHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[0], localSettingsService, WorkspaceLayoutSettingsKeys.HistorySessionListWidth, defaultWidth: 300);
        _fileListPaneHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[2], localSettingsService, WorkspaceLayoutSettingsKeys.HistoryFileListWidth, defaultWidth: 260);

        Loaded += async (_, _) =>
        {
            await _sessionListPaneHelper.RestoreAsync();
            await _fileListPaneHelper.RestoreAsync();
        };
        SessionListSplitter.DragCompleted += (_, _) => _sessionListPaneHelper.OnSplitterDragCompleted();
        HistoryFileListSplitter.DragCompleted += (_, _) => _fileListPaneHelper.OnSplitterDragCompleted();
    }
}
