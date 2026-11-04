using Avalonia.Controls;
using Avalonia.Interactivity;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Helpers;
using Paarde.Avalonia.Models;
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
    // the two columns resize/persist independently. Not readonly/constructed eagerly like
    // _paneHelper above - Epic 26 (specs/interface-mode R7) needs an InterfaceMode read (async)
    // to pick the right first-run default before this can be constructed, so it's assigned later
    // from InitializeAssistantPanelHelperAsync instead; null until that completes.
    private ResizablePaneHelper? _assistantPanelHelper;

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

        Loaded += async (_, _) => await InitializeAssistantPanelHelperAsync();
        AssistantPanelSplitter.DragCompleted += (_, _) => _assistantPanelHelper?.OnSplitterDragCompleted();
    }

    // Epic 26 (specs/interface-mode R7): FlowWizard mode has no Compose/Review page reachable
    // from here (they're separate navigation destinations in that mode, not a docked panel), so
    // the Assistant panel's first-run default width collapses to 0 instead of 360 - only affects
    // users with no saved width yet, same as _paneHelper's own narrower default above.
    private async Task InitializeAssistantPanelHelperAsync()
    {
        var localSettingsService = App.GetService<ILocalSettingsService>();
        var interfaceMode = await localSettingsService.ReadSettingAsync<InterfaceMode?>(InterfaceModeSettingsKeys.Mode) ?? InterfaceMode.EditorFocused;
        var defaultWidth = interfaceMode == InterfaceMode.FlowWizard ? 0 : 360;

        _assistantPanelHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[4], localSettingsService, WorkspaceLayoutSettingsKeys.ExplorerAssistantPanelWidth, defaultWidth: defaultWidth);
        await _assistantPanelHelper.RestoreAsync();
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

        _assistantPanelHelper?.OnSplitterDragCompleted();
    }
}
