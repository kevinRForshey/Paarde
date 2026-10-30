using Avalonia.Controls;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Helpers;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.ViewModels;

namespace Paarde.Avalonia.Views;

public partial class ExplorerView : UserControl
{
    public ExplorerViewModel ViewModel
    {
        get;
    }

    private readonly ResizablePaneHelper _paneHelper;

    public ExplorerView()
    {
        ViewModel = App.GetService<ExplorerViewModel>();
        DataContext = ViewModel;
        InitializeComponent();

        _paneHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[0], App.GetService<ILocalSettingsService>(), WorkspaceLayoutSettingsKeys.ExplorerTreeWidth, defaultWidth: 300);
        Loaded += async (_, _) => await _paneHelper.RestoreAsync();
        TreeSplitter.DragCompleted += (_, _) => _paneHelper.OnSplitterDragCompleted();
    }
}
