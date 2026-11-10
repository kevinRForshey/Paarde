using Avalonia.Controls;

using Paarde.Presentation.Contracts.Services;

namespace Paarde.Avalonia.Helpers;

// Restores and persists one Grid column's width (a master/browse-list pane) so every
// master-detail page (Explorer, Diffs, Tutorial, History) shares the same GridSplitter +
// ILocalSettingsService plumbing instead of duplicating it per view. Dragging a pane all the way
// shut (width 0) and reopening it is this app's "collapsible" - there's no separate collapsed flag
// to keep in sync with the dragged width.
public sealed class ResizablePaneHelper
{
    private readonly ColumnDefinition _column;
    private readonly ILocalSettingsService _settingsService;
    private readonly string _widthSettingKey;
    private readonly double _defaultWidth;
    private readonly bool _startsCollapsed;
    private double? _widthBeforeCollapse;

    public ResizablePaneHelper(ColumnDefinition column, ILocalSettingsService settingsService, string widthSettingKey, double defaultWidth, bool startsCollapsed = false)
    {
        _column = column;
        _settingsService = settingsService;
        _widthSettingKey = widthSettingKey;
        _defaultWidth = defaultWidth;
        _startsCollapsed = startsCollapsed;
    }

    public async Task RestoreAsync()
    {
        var width = await _settingsService.ReadSettingAsync<double?>(_widthSettingKey) ?? (_startsCollapsed ? 0 : _defaultWidth);
        _column.Width = new GridLength(Math.Max(width, 0));
    }

    public async void OnSplitterDragCompleted() => await _settingsService.SaveSettingAsync(_widthSettingKey, _column.Width.Value);

    public bool IsCollapsed => _column.Width.Value <= 0;

    // A reusable collapse/remember-width/restore toggle, replacing
    // the "remembered width" field each caller (e.g. ExplorerView.axaml.cs's Assistant panel
    // button) used to keep ad-hoc - zen mode needs the same mechanism applied to two panes at
    // once, so it's a method on this class rather than duplicated per call site.
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
        // defaultWidth, not 0, when the pane started collapsed or was saved shut - otherwise it
        // could never be reopened.
        _column.Width = new GridLength(_widthBeforeCollapse is > 0 ? _widthBeforeCollapse.Value : _defaultWidth);
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
