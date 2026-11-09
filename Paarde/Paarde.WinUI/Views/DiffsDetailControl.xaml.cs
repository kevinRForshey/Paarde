using System.ComponentModel;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Paarde.Core.Helpers;
using Paarde.Core.Models;
using Paarde.Core.ViewModels;

using Windows.ApplicationModel.DataTransfer;

namespace Paarde.WinUI.Views;

// A display row plus the file it belongs to. Avalonia's template reached the file path through
// $parent[UserControl].DataContext; WinUI DataTemplates can't see outside themselves, so each row
// carries it instead.
public sealed record DiffRowItem(CollapsibleDiffRow Row, string? FilePath);

public sealed record SideBySideRowItem(CollapsibleSideBySideDiffRow Row, string? FilePath);

public sealed partial class DiffsDetailControl : UserControl
{
    private ProposedFileChangeViewModel? _subscribedViewModel;

    // T8.3.2: which collapsed regions (keyed by LineIndex) the user has expanded back to their real
    // lines, for the file currently shown. Reset whenever DiffLines changes, since a new diff means
    // those indices no longer necessarily point at the same regions.
    private readonly HashSet<int> _expandedSegments = [];

    // T8.3.2: the inline view's current rows, so ScrollToHunk can look one up by LineIndex rather
    // than assuming a DiffLines index maps 1:1 onto a display row index.
    private IReadOnlyList<CollapsibleDiffRow> _inlineRows = [];
    private List<DiffRowItem> _inlineItems = [];

    // T8.3.4: the current file's hunks and which one next/previous-change navigation last scrolled
    // to - -1 means "haven't navigated to one yet".
    private IReadOnlyList<DiffHunk> _hunks = [];
    private int _currentHunkIndex = -1;

    public ProposedFileChangeViewModel? ViewModel => _subscribedViewModel;

    public DiffsDetailControl()
    {
        InitializeComponent();

        // T8.3.1/T8.3.2: recomputes both views' rows whenever the file shown changes (switching
        // files reuses this one control instance) or that file's DiffLines changes.
        DataContextChanged += (_, _) => OnDataContextChanged();
    }

    private void OnCopyButtonClick(object sender, RoutedEventArgs e)
    {
        if (_subscribedViewModel is null)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(_subscribedViewModel.ProposedContent);
        Clipboard.SetContent(package);
    }

    // T8.3.1: Inline/side-by-side is local UI state (not persisted, not on the shared Core view
    // model, which multiple pages bind to).
    private void OnViewModeToggled(object sender, RoutedEventArgs e) => ShowSideBySide(SideBySideToggle.IsChecked == true);

    private void ShowSideBySide(bool isSideBySide)
    {
        InlineScrollViewer.Visibility = isSideBySide ? Visibility.Collapsed : Visibility.Visible;
        SideBySideScrollViewer.Visibility = isSideBySide ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDataContextChanged()
    {
        if (ReferenceEquals(DataContext, _subscribedViewModel))
        {
            return;
        }

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = DataContext as ProposedFileChangeViewModel;

        // T8.3.2: switching files invalidates any expanded-region indices from the previous file.
        _expandedSegments.Clear();

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        Bindings.Update();
        RefreshDisplayRows();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProposedFileChangeViewModel.DiffLines))
        {
            _expandedSegments.Clear();
            RefreshDisplayRows();
        }
    }

    private void RefreshDisplayRows()
    {
        var diffLines = _subscribedViewModel?.DiffLines ?? Array.Empty<DiffLine>();
        var filePath = _subscribedViewModel?.FilePath;

        _inlineRows = DiffBuilder.BuildCollapsibleRows(diffLines, _expandedSegments);
        _inlineItems = _inlineRows.Select(row => new DiffRowItem(row, filePath)).ToList();
        DiffLinesList.ItemsSource = _inlineItems;
        SideBySideList.ItemsSource = DiffBuilder.BuildCollapsibleSideBySideRows(diffLines, _expandedSegments)
            .Select(row => new SideBySideRowItem(row, filePath))
            .ToList();

        // T8.3.4: a previous hunk index isn't guaranteed to still mean the same change once
        // DiffLines has been recomputed, so navigation restarts from "haven't navigated yet".
        _hunks = DiffBuilder.GroupIntoHunks(diffLines);
        _currentHunkIndex = -1;
        UpdateChangeNavigationState();
    }

    // T8.3.2: shared by both views' collapsed-region placeholders - expanding re-renders that
    // region's real lines in place.
    private void OnExpandCollapsedRegionClick(object sender, RoutedEventArgs e)
    {
        ICollapsedDiffRegion? region = (sender as FrameworkElement)?.DataContext switch
        {
            DiffRowItem item => item.Row,
            SideBySideRowItem item => item.Row,
            _ => null,
        };

        if (region is { IsCollapsedPlaceholder: true })
        {
            _expandedSegments.Add(region.LineIndex);
            RefreshDisplayRows();
        }
    }

    // T8.3.4: scrolls the inline view to one of the current file's hunks - always switches to
    // inline first, since the side-by-side view has no equivalent scroll target.
    private void ScrollToHunk(int hunkIndex)
    {
        if (hunkIndex < 0 || hunkIndex >= _hunks.Count)
        {
            return;
        }

        _currentHunkIndex = hunkIndex;

        SideBySideToggle.IsChecked = false;
        ShowSideBySide(false);

        var displayIndex = FindDisplayIndex(_hunks[hunkIndex].SourceRangeStart);

        if (displayIndex >= 0)
        {
            DiffLinesList.ScrollIntoView(_inlineItems[displayIndex], ScrollIntoViewAlignment.Leading);
        }

        UpdateChangeNavigationState();
    }

    private void OnNextChangeClick(object sender, RoutedEventArgs e)
    {
        if (_hunks.Count > 0)
        {
            ScrollToHunk(Math.Min(_currentHunkIndex + 1, _hunks.Count - 1));
        }
    }

    private void OnPreviousChangeClick(object sender, RoutedEventArgs e)
    {
        if (_hunks.Count > 0)
        {
            ScrollToHunk(Math.Max(_currentHunkIndex - 1, 0));
        }
    }

    // T8.3.2/T8.3.4: a hunk's SourceRangeStart indexes DiffLines, which no longer maps 1:1 onto
    // display rows once collapsing is involved - look the row up by LineIndex instead. It's always
    // its own row since BuildCollapsibleRows keeps the same context lines GroupIntoHunks does.
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
