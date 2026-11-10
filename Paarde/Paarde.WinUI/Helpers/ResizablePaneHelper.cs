using CommunityToolkit.WinUI.Controls;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Paarde.Presentation.Contracts.Services;

namespace Paarde.WinUI.Helpers;

// Restores and persists one Grid column's width (a master/browse-list pane) - port of
// Paarde.Avalonia's ResizablePaneHelper, reading/writing the same WorkspaceLayoutSettingsKeys so a
// pane width carries over between the two apps. Dragging a pane all the way shut (width 0) and
// reopening it is this app's "collapsible" - there's no separate collapsed flag to keep in sync.
public sealed class ResizablePaneHelper
{
    private readonly ColumnDefinition _column;
    private readonly ILocalSettingsService _settingsService;
    private readonly string _widthSettingKey;
    private readonly double _defaultWidth;
    private readonly bool _startsCollapsed;
    private readonly UIElement? _pane;
    private double? _widthBeforeCollapse;

    public ResizablePaneHelper(ColumnDefinition column, ILocalSettingsService settingsService, string widthSettingKey, double defaultWidth, bool startsCollapsed = false, UIElement? pane = null)
    {
        _column = column;
        _settingsService = settingsService;
        _widthSettingKey = widthSettingKey;
        _defaultWidth = defaultWidth;
        _startsCollapsed = startsCollapsed;
        _pane = pane;
    }

    // Persists after each drag. The toolkit's GridSplitter handles its own manipulation events, so
    // the handler is registered with handledEventsToo; there's no DragCompleted like Avalonia's.
    public void AttachSplitter(GridSplitter splitter) =>
        splitter.AddHandler(UIElement.ManipulationCompletedEvent, new ManipulationCompletedEventHandler((_, _) => OnSplitterDragCompleted()), handledEventsToo: true);

    public async Task RestoreAsync()
    {
        var width = await _settingsService.ReadSettingAsync<double?>(_widthSettingKey) ?? (_startsCollapsed ? 0 : _defaultWidth);
        SetWidth(Math.Max(width, 0));
    }

    public async void OnSplitterDragCompleted()
    {
        UpdatePaneVisibility(_column.ActualWidth);
        await _settingsService.SaveSettingAsync(_widthSettingKey, _column.ActualWidth);
    }

    // After a toggle rather than a drag: layout hasn't run yet, so ActualWidth is still the old width.
    public async void PersistWidth() => await _settingsService.SaveSettingAsync(_widthSettingKey, _column.Width.Value);

    public bool IsCollapsed => _column.Width.Value <= 0;

    // collapse/remember-width/restore toggle (used by Explorer, F31.7).
    public void Collapse()
    {
        if (IsCollapsed)
        {
            return;
        }

        _widthBeforeCollapse = _column.Width.Value;
        SetWidth(0);
    }

    public void Restore()
    {
        // defaultWidth, not 0, when the pane started collapsed or was saved shut - otherwise it
        // could never be reopened.
        SetWidth(_widthBeforeCollapse is > 0 ? _widthBeforeCollapse.Value : _defaultWidth);
        _widthBeforeCollapse = null;
    }

    private void SetWidth(double width)
    {
        _column.Width = new GridLength(width);
        UpdatePaneVisibility(width);
    }

    // A 0-wide column still holds its controls: Tab and screen readers reach them unless the pane
    // is collapsed too.
    private void UpdatePaneVisibility(double width)
    {
        if (_pane != null)
        {
            _pane.Visibility = width > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
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
