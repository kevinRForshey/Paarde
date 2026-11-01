using Avalonia.Controls;
using Avalonia.Interactivity;

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

    // Epic 25, T12 (R2): same ResizablePaneHelper as the tree pane, keyed to its own setting so
    // the two columns resize/persist independently.
    private readonly ResizablePaneHelper _assistantPanelHelper;

    // Remembers the width the Assistant panel had before the toggle button collapsed it, since
    // ResizablePaneHelper's own "width 0 is collapsed" convention has no separate flag to restore
    // from - collapsing from a drag has nothing to remember, but collapsing from this button does.
    private double _assistantPanelWidthBeforeCollapse = 360;

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

        _assistantPanelHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[4], App.GetService<ILocalSettingsService>(), WorkspaceLayoutSettingsKeys.ExplorerAssistantPanelWidth, defaultWidth: 360);
        Loaded += async (_, _) => await _assistantPanelHelper.RestoreAsync();
        AssistantPanelSplitter.DragCompleted += (_, _) => _assistantPanelHelper.OnSplitterDragCompleted();
    }

    private void OnToggleAssistantPanelButtonClick(object? sender, RoutedEventArgs e)
    {
        var column = WorkspaceGrid.ColumnDefinitions[4];

        if (column.Width.Value > 0)
        {
            _assistantPanelWidthBeforeCollapse = column.Width.Value;
            column.Width = new GridLength(0);
        }
        else
        {
            column.Width = new GridLength(_assistantPanelWidthBeforeCollapse);
        }

        _assistantPanelHelper.OnSplitterDragCompleted();
    }
}
