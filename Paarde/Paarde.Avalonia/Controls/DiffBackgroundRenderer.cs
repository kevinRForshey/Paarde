using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Media;

using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

using Paarde.Core.Models;
using Paarde.Presentation.Helpers;

namespace Paarde.Avalonia.Controls;

// Paints translucent green bands behind added lines and a thin red marker at each pure deletion's
// insertion point, directly over the editable proposal view (TutorialDetailControl's editor) the
// same way BracketHighlightRenderer paints bracket matches - same IBackgroundRenderer extension
// point, no new one introduced. Reuses DiffBuilder's existing line-level diff
// (ProposedFileChangeViewModel.DiffLines) rather than a second diff algorithm. The line-range
// mapping itself is Paarde.Presentation's DiffBandBuilder (shared with the WinUI editor); only the
// paint calls live here.
internal sealed class DiffBackgroundRenderer : IBackgroundRenderer
{
    private readonly TextView _textView;
    private IReadOnlyList<DiffLine> _diffLines = Array.Empty<DiffLine>();

    public KnownLayer Layer => KnownLayer.Background;

    public IBrush AddedBrush { get; set; } = new SolidColorBrush(Color.FromArgb(60, 46, 160, 67));

    public IBrush RemovedMarkerBrush { get; set; } = new SolidColorBrush(Color.FromArgb(200, 203, 36, 49));

    public DiffBackgroundRenderer(TextView textView)
    {
        _textView = textView;
        _textView.BackgroundRenderers.Add(this);
    }

    // T13.2.1: replaces _diffLines wholesale every time the bound DiffLines change (an edit, or
    // OriginalContent being refreshed from disk) rather than caching offsets once at load, so the
    // bands painted next never describe a stale diff.
    public void UpdateDiffLines(IReadOnlyList<DiffLine>? diffLines)
    {
        _diffLines = diffLines ?? Array.Empty<DiffLine>();
        _textView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_diffLines.Count == 0 || textView.Document is null || !textView.VisualLinesValid)
        {
            return;
        }

        foreach (var band in DiffBandBuilder.Build(_diffLines))
        {
            if (band.IsAddition)
            {
                DrawAddedBand(textView, drawingContext, band);
            }
            else
            {
                DrawRemovedMarker(textView, drawingContext, band.StartLine);
            }
        }
    }

    private void DrawAddedBand(TextView textView, DrawingContext drawingContext, DiffBand band)
    {
        var document = textView.Document;
        if (band.StartLine < 1 || band.StartLine > document.LineCount)
        {
            return;
        }

        var endLine = Math.Min(band.StartLine + band.LineCount - 1, document.LineCount);
        var startOffset = document.GetLineByNumber(band.StartLine).Offset;
        var endLineInfo = document.GetLineByNumber(endLine);

        var builder = new BackgroundGeometryBuilder
        {
            AlignToWholePixels = true,
            CornerRadius = 0,
            ExtendToFullWidthAtLineEnd = true,
        };
        builder.AddSegment(textView, new TextSegment { StartOffset = startOffset, Length = endLineInfo.EndOffset - startOffset });

        var geometry = builder.CreateGeometry();
        if (geometry != null)
        {
            drawingContext.DrawGeometry(AddedBrush, null, geometry);
        }
    }

    private void DrawRemovedMarker(TextView textView, DrawingContext drawingContext, int anchorLine)
    {
        var document = textView.Document;
        var lineCount = document.LineCount;
        if (lineCount == 0)
        {
            return;
        }

        var targetLine = Math.Clamp(anchorLine, 1, lineCount);
        var visualLine = textView.GetVisualLine(targetLine);
        if (visualLine is null)
        {
            // Not currently scrolled into view - nothing to draw this frame, same as how AddSegment
            // above silently produces no geometry for offscreen offsets.
            return;
        }

        // A deletion past the last line (content removed from the end of the file) renders below
        // that line instead of above it; every other case renders above the anchor line, matching
        // where the removed content used to sit relative to what's left.
        var y = anchorLine > lineCount ? visualLine.VisualTop + visualLine.Height : visualLine.VisualTop;
        y -= textView.ScrollOffset.Y;

        var builder = new BackgroundGeometryBuilder();
        builder.AddRectangle(textView, new Rect(0, y - 1, textView.Bounds.Width, 2));

        var geometry = builder.CreateGeometry();
        if (geometry != null)
        {
            drawingContext.DrawGeometry(RemovedMarkerBrush, null, geometry);
        }
    }

}
