using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Interactivity;

using Paarde.Core.Helpers;
using Paarde.Core.Models;
using Paarde.Core.ViewModels;

namespace Paarde.Avalonia.Views;

public partial class DiffsDetailControl : UserControl
{
    public DiffsDetailControl()
    {
        InitializeComponent();

        // T8.3.1/T8.3.2: recomputes both views' display rows whenever the file being shown changes
        // (switching files reuses this one control instance) or that file's DiffLines changes (an
        // edit, or OriginalContent refreshing from disk) - kept up to date even while the other view
        // is the one currently visible, so toggling modes never shows stale rows.
        DataContextChanged += (_, _) => OnDataContextChanged();
        OnDataContextChanged();
    }

    private ProposedFileChangeViewModel? _subscribedViewModel;

    // T8.3.2: which collapsed regions (keyed by CollapsibleDiffRow.LineIndex) the user has expanded
    // back to their real lines, for the file currently shown. Reset whenever DiffLines changes for
    // any reason (see OnDataContextChanged/OnViewModelPropertyChanged below) since a new diff means
    // those indices no longer necessarily point at the same regions.
    private readonly HashSet<int> _expandedSegments = [];

    // T8.3.2: the inline view's current rows, cached so ApplyPendingScrollTarget/ScrollToHunk can
    // look one up by LineIndex rather than assuming a DiffLines index maps 1:1 onto a display row
    // index.
    private IReadOnlyList<CollapsibleDiffRow> _inlineRows = [];

    // T8.3.4: the current file's hunks (same grouping GroupIntoHunks already provides for T8.4.5's
    // jump-from-tutorial-step feature) and which one next/previous-change navigation last scrolled
    // to, as an index into this list - -1 means "haven't navigated to one yet".
    private IReadOnlyList<DiffHunk> _hunks = [];
    private int _currentHunkIndex = -1;

    private async void OnCopyButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProposedFileChangeViewModel proposedFileChange)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(proposedFileChange.ProposedContent);
        }
    }

    // T8.3.1: Inline/side-by-side is local UI state (not persisted, not on the shared Core view
    // model, which multiple pages bind to) - a ToggleButton rather than a settings-backed flag,
    // same "no separate state to keep in sync" reasoning as T8.2.3's collapsible panes just
    // applied to a two-way local switch instead.
    private void OnViewModeToggled(object? sender, RoutedEventArgs e)
    {
        var isSideBySide = SideBySideToggle.IsChecked == true;
        DiffLinesItemsControl.IsVisible = !isSideBySide;
        SideBySideScrollViewer.IsVisible = isSideBySide;
    }

    private void OnDataContextChanged()
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = DataContext as ProposedFileChangeViewModel;

        // T8.3.2: switching files invalidates any expanded-region indices from the previous file's
        // DiffLines.
        _expandedSegments.Clear();

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RefreshDisplayRows();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProposedFileChangeViewModel.DiffLines))
        {
            // T8.3.2: an edit or an original-content refresh means DiffLines was recomputed from
            // scratch, so no previously expanded index is guaranteed to still refer to the same
            // region.
            _expandedSegments.Clear();
            RefreshDisplayRows();
        }
    }

    private void RefreshDisplayRows()
    {
        var diffLines = _subscribedViewModel?.DiffLines ?? Array.Empty<DiffLine>();

        _inlineRows = DiffBuilder.BuildCollapsibleRows(diffLines, _expandedSegments);
        DiffLinesItemsControl.ItemsSource = _inlineRows;
        SideBySideItemsControl.ItemsSource = DiffBuilder.BuildCollapsibleSideBySideRows(diffLines, _expandedSegments);

        // T8.3.4: a previous hunk index isn't guaranteed to still mean the same change once
        // DiffLines has been recomputed (a file switch or an edit), so next/previous navigation
        // always restarts from "haven't navigated to one yet".
        _hunks = DiffBuilder.GroupIntoHunks(diffLines);
        _currentHunkIndex = -1;
        UpdateChangeNavigationState();
    }

    // T8.3.2: shared by both views' collapsed-region placeholders (CollapsibleDiffRow and
    // CollapsibleSideBySideDiffRow both implement ICollapsedDiffRegion) - expanding re-renders that
    // region's real lines in place rather than navigating anywhere.
    private void OnExpandCollapsedRegionClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is ICollapsedDiffRegion { IsCollapsedPlaceholder: true } region)
        {
            _expandedSegments.Add(region.LineIndex);
            RefreshDisplayRows();
        }
    }

    // T8.3.4: scrolls the inline view to one of the current file's hunks by index - shared by the
    // Next/Previous change buttons. Always switches to inline first - the side-by-side view has no
    // equivalent index-based scroll target.
    private void ScrollToHunk(int hunkIndex)
    {
        if (hunkIndex < 0 || hunkIndex >= _hunks.Count)
        {
            return;
        }

        _currentHunkIndex = hunkIndex;

        SideBySideToggle.IsChecked = false;
        DiffLinesItemsControl.IsVisible = true;
        SideBySideScrollViewer.IsVisible = false;

        var displayIndex = FindDisplayIndex(_hunks[hunkIndex].SourceRangeStart);

        if (displayIndex >= 0)
        {
            DiffLinesItemsControl.ScrollIntoView(displayIndex);
        }

        UpdateChangeNavigationState();
    }

    private void OnNextChangeClick(object? sender, RoutedEventArgs e)
    {
        if (_hunks.Count > 0)
        {
            ScrollToHunk(Math.Min(_currentHunkIndex + 1, _hunks.Count - 1));
        }
    }

    private void OnPreviousChangeClick(object? sender, RoutedEventArgs e)
    {
        if (_hunks.Count > 0)
        {
            ScrollToHunk(Math.Max(_currentHunkIndex - 1, 0));
        }
    }

    // T8.3.2/T8.3.4: target.SourceRangeStart (or a hunk's own SourceRangeStart) is an index into
    // DiffLines, which no longer maps 1:1 onto a display row index once collapsing is involved -
    // look the row up by LineIndex instead. It's always present as its own row (never folded into a
    // placeholder) because BuildCollapsibleRows defaults to the same contextLines as
    // GroupIntoHunks, so a hunk's own leading context is exactly the context kept visible here.
    private int FindDisplayIndex(int lineIndex)
    {
        for (var i = 0; i < _inlineRows.Count; i++)
        {
            if (_inlineRows[i].LineIndex == lineIndex)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindHunkIndex(int sourceRangeStart)
    {
        for (var i = 0; i < _hunks.Count; i++)
        {
            if (_hunks[i].SourceRangeStart == sourceRangeStart)
            {
                return i;
            }
        }

        return -1;
    }

    private void UpdateChangeNavigationState()
    {
        PreviousChangeButton.IsEnabled = _currentHunkIndex > 0;
        NextChangeButton.IsEnabled = _currentHunkIndex < _hunks.Count - 1;

        ChangeNavigationStatusText.Text = _hunks.Count switch
        {
            0 => "No changes",
            _ when _currentHunkIndex < 0 => $"{_hunks.Count} change{(_hunks.Count == 1 ? "" : "s")}",
            _ => $"Change {_currentHunkIndex + 1} of {_hunks.Count}",
        };
    }
}
