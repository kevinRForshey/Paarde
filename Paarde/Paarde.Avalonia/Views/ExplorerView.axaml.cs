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

        // UX pass: narrower default so the editor gets more width on first run - only affects
        // users with no saved width yet; TreeSplitter stays fully resizable either way.
        _paneHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[0], App.GetService<ILocalSettingsService>(), WorkspaceLayoutSettingsKeys.ExplorerTreeWidth, defaultWidth: 220);
        Loaded += async (_, _) => await _paneHelper.RestoreAsync();
        TreeSplitter.DragCompleted += (_, _) => _paneHelper.OnSplitterDragCompleted();
    }
}
