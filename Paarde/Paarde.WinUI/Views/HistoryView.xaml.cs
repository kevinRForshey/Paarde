using Microsoft.UI.Xaml.Controls;

using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;
using Paarde.WinUI.Helpers;

namespace Paarde.WinUI.Views;

public sealed partial class HistoryView : Page
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
        InitializeComponent();

        var localSettingsService = App.GetService<ILocalSettingsService>();
        _sessionListPaneHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[0], localSettingsService, WorkspaceLayoutSettingsKeys.HistorySessionListWidth, defaultWidth: 300);
        _fileListPaneHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[2], localSettingsService, WorkspaceLayoutSettingsKeys.HistoryFileListWidth, defaultWidth: 260);
        _sessionListPaneHelper.AttachSplitter(SessionListSplitter);
        _fileListPaneHelper.AttachSplitter(HistoryFileListSplitter);

        Loaded += async (_, _) =>
        {
            await _sessionListPaneHelper.RestoreAsync();
            await _fileListPaneHelper.RestoreAsync();
        };
    }
}
