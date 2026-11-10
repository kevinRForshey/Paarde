using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;

using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

using Paarde.Core.Models;

namespace Paarde.Avalonia.Controls;

public sealed class DiffHunkAcceptRejectEventArgs(DiffHunk hunk, bool accept) : EventArgs
{
    public DiffHunk Hunk { get; } = hunk;

    public bool Accept { get; } = accept;
}

// Left-margin gutter: one Accept/Reject button pair per pending diff hunk that's currently
// scrolled into view - an alternate entry point into the same per-step selection
// (ProposedFileChangeViewModel.SetDiffHunkSelection, driving ProposedHunk.IsSelected) the Tutorial
// page's step checkboxes already use, not a second accept/reject model. Follows the same
// pattern as AvaloniaEdit's own FoldingMargin: one real child Control per visible anchor, rebuilt
// whenever the visible VisualLines change (scroll, resize, or an edit reflowing the document) rather
// than trying to keep stale child controls positioned by hand. Collapses to zero width when there
// are no hunks, so other CodeEditor hosts that never set DiffHunks (e.g. Explorer's read-only
// preview) are unaffected.
public sealed class DiffHunkGutterMargin : AbstractMargin
{
    private const double ButtonColumnWidth = 44;

    private readonly List<(VisualLine VisualLine, StackPanel Panel)> _entries = new();

    private IReadOnlyList<DiffHunk> _hunks = Array.Empty<DiffHunk>();

    public event EventHandler<DiffHunkAcceptRejectEventArgs>? HunkActionRequested;

    // Replaces _hunks wholesale (never patched in place) so a rebuild always reflects the
    // current diff, including after an edit re-anchors every hunk's line range.
    public void UpdateHunks(IReadOnlyList<DiffHunk>? hunks)
    {
        _hunks = hunks ?? Array.Empty<DiffHunk>();
        RebuildEntries();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var (_, panel) in _entries)
        {
            panel.Measure(availableSize);
        }

        return new Size(_hunks.Count == 0 ? 0 : ButtonColumnWidth, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var scrollOffsetY = TextView?.ScrollOffset.Y ?? 0;

        foreach (var (visualLine, panel) in _entries)
        {
            var y = visualLine.VisualTop - scrollOffsetY;
            panel.Arrange(new Rect(0, y, finalSize.Width, visualLine.Height));
        }

        return finalSize;
    }

    protected override void OnTextViewVisualLinesChanged()
    {
        RebuildEntries();
        base.OnTextViewVisualLinesChanged();
    }

    private void RebuildEntries()
    {
        foreach (var (_, panel) in _entries)
        {
            VisualChildren.Remove(panel);
            LogicalChildren.Remove(panel);
        }

        _entries.Clear();

        if (TextView is { VisualLinesValid: true } textView && _hunks.Count > 0)
        {
            foreach (var hunk in _hunks)
            {
                var anchorLine = Math.Max(hunk.ProposedStartLine, 1);
                var visualLine = textView.VisualLines.FirstOrDefault(vl =>
                    anchorLine >= vl.FirstDocumentLine.LineNumber && anchorLine <= vl.LastDocumentLine.LineNumber);

                if (visualLine is null)
                {
                    continue;
                }

                var panel = CreateButtonPanel(hunk);
                ((ISetLogicalParent)panel).SetParent(this);
                VisualChildren.Add(panel);
                LogicalChildren.Add(panel);
                _entries.Add((visualLine, panel));
            }
        }

        InvalidateMeasure();
        InvalidateArrange();
    }

    private StackPanel CreateButtonPanel(DiffHunk hunk)
    {
        var acceptButton = new Button { Content = "✓", Padding = new Thickness(4, 0), MinWidth = 0 };
        ToolTip.SetTip(acceptButton, "Accept this change.");
        acceptButton.Click += (_, _) => HunkActionRequested?.Invoke(this, new DiffHunkAcceptRejectEventArgs(hunk, accept: true));

        var rejectButton = new Button { Content = "✗", Padding = new Thickness(4, 0), MinWidth = 0 };
        ToolTip.SetTip(rejectButton, "Reject this change.");
        rejectButton.Click += (_, _) => HunkActionRequested?.Invoke(this, new DiffHunkAcceptRejectEventArgs(hunk, accept: false));

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { acceptButton, rejectButton },
        };
    }
}
