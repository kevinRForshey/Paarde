using CommunityToolkit.WinUI.Controls;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Paarde.Presentation.Contracts.Services;

namespace Paarde.WinUI.Helpers;

// T8.2.3: restores and persists one Grid column's width (a master/browse-list pane) - port of
// Paarde.Avalonia's ResizablePaneHelper, reading/writing the same WorkspaceLayoutSettingsKeys so a
// pane width carries over between the two apps. Dragging a pane all the way shut (width 0) and
// reopening it is this app's "collapsible" - there's no separate collapsed flag to keep in sync.
public sealed class ResizablePaneHelper
{
    private readonly ColumnDefinition _column;
    private readonly ILocalSettingsService _settingsService;
    private readonly string _widthSettingKey;
    private readonly double _defaultWidth;
    private double? _widthBeforeCollapse;

    public ResizablePaneHelper(ColumnDefinition column, ILocalSettingsService settingsService, string widthSettingKey, double defaultWidth)
    {
        _column = column;
        _settingsService = settingsService;
        _widthSettingKey = widthSettingKey;
        _defaultWidth = defaultWidth;
    }

    // Persists after each drag. The toolkit's GridSplitter handles its own manipulation events, so
    // the handler is registered with handledEventsToo; there's no DragCompleted like Avalonia's.
    public void AttachSplitter(GridSplitter splitter) =>
        splitter.AddHandler(UIElement.ManipulationCompletedEvent, new ManipulationCompletedEventHandler((_, _) => OnSplitterDragCompleted()), handledEventsToo: true);

    public async Task RestoreAsync()
    {
        var width = await _settingsService.ReadSettingAsync<double?>(_widthSettingKey) ?? _defaultWidth;
        _column.Width = new GridLength(Math.Max(width, 0));
    }

    public async void OnSplitterDragCompleted() => await _settingsService.SaveSettingAsync(_widthSettingKey, _column.ActualWidth);

    public bool IsCollapsed => _column.Width.Value <= 0;

    // specs/editor-focus-layout R1: collapse/remember-width/restore toggle (used by Explorer, F31.7).
    public void Collapse()
    {
        if (IsCollapsed)
        {
            return;
        }

        _widthBeforeCollapse = _column.Width.Value;
        _column.Width = new GridLength(0);
    }

    public void Restore()
    {
        _column.Width = new GridLength(_widthBeforeCollapse ?? _defaultWidth);
        _widthBeforeCollapse = null;
    }

    public void ToggleCollapse()
    {
        if (IsCollapsed)
        {
            Restore();
        }
        else
        {
            Collapse();
        }
    }
}
