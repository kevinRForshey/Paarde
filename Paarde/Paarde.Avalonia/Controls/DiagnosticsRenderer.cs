using Avalonia;
using Avalonia.Media;

using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

using Paarde.Core.Models;

namespace Paarde.Avalonia.Controls;

// Epic 17, T17.1.2: underlines a server-reported diagnostic's range, same IBackgroundRenderer
// extension point as BracketHighlightRenderer/DiffBackgroundRenderer. Deliberately a straight
// line, not a wavy squiggle - the color already conveys severity, and a true squiggle glyph isn't
// worth the added geometry complexity for what's otherwise the same underline shape.
internal sealed class DiagnosticsRenderer : IBackgroundRenderer
{
    private readonly TextView _textView;
    private IReadOnlyList<LspDiagnostic> _diagnostics = Array.Empty<LspDiagnostic>();

    public KnownLayer Layer => KnownLayer.Selection;

    public IBrush ErrorBrush { get; set; } = new SolidColorBrush(Color.FromRgb(224, 64, 64));

    public IBrush WarningBrush { get; set; } = new SolidColorBrush(Color.FromRgb(204, 164, 61));

    public IBrush InfoBrush { get; set; } = new SolidColorBrush(Color.FromRgb(128, 128, 128));

    public DiagnosticsRenderer(TextView textView)
    {
        _textView = textView;
        _textView.BackgroundRenderers.Add(this);
    }

    public void UpdateDiagnostics(IReadOnlyList<LspDiagnostic>? diagnostics)
    {
        _diagnostics = diagnostics ?? Array.Empty<LspDiagnostic>();
        _textView.InvalidateLayer(Layer);
    }

    // Finds whichever diagnostic (if any) covers documentOffset - used by CodeEditor to decide
    // what hover tooltip text, if any, to show at the pointer's current position.
    public LspDiagnostic? FindAt(TextDocument document, int documentOffset)
    {
        foreach (var diagnostic in _diagnostics)
        {
            var (startOffset, endOffset) = ToOffsetRange(document, diagnostic);
            if (documentOffset >= startOffset && documentOffset < endOffset)
            {
                return diagnostic;
            }
        }

        return null;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_diagnostics.Count == 0 || textView.Document is null || !textView.VisualLinesValid)
        {
            return;
        }

        foreach (var diagnostic in _diagnostics)
        {
            DrawUnderline(textView, drawingContext, diagnostic);
        }
    }

    private void DrawUnderline(TextView textView, DrawingContext drawingContext, LspDiagnostic diagnostic)
    {
        var document = textView.Document;
        var (startOffset, endOffset) = ToOffsetRange(document, diagnostic);
        if (endOffset <= startOffset)
        {
            return;
        }

        var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 0 };
        builder.AddSegment(textView, new TextSegment { StartOffset = startOffset, Length = endOffset - startOffset });

        var geometry = builder.CreateGeometry();
        if (geometry is null)
        {
            return;
        }

        var brush = diagnostic.Severity switch
        {
            LspDiagnosticSeverity.Error => ErrorBrush,
            LspDiagnosticSeverity.Warning => WarningBrush,
            _ => InfoBrush,
        };

        var bounds = geometry.Bounds;
        var pen = new Pen(brush, 1.5);
        drawingContext.DrawLine(pen, new Point(bounds.Left, bounds.Bottom - 1), new Point(bounds.Right, bounds.Bottom - 1));
    }

    // Diagnostics spanning multiple lines are clamped to Start's line - the vast majority of
    // compiler diagnostics are single-line, and a multi-line underline would need per-line segments
    // (one BackgroundGeometryBuilder.AddSegment call per visual line) that nothing here needs yet.
    private static (int Start, int End) ToOffsetRange(TextDocument document, LspDiagnostic diagnostic)
    {
        if (diagnostic.Start.Line < 0 || diagnostic.Start.Line >= document.LineCount)
        {
            return (0, 0);
        }

        var startLine = document.GetLineByNumber(diagnostic.Start.Line + 1);
        var startOffset = Math.Clamp(startLine.Offset + diagnostic.Start.Character, startLine.Offset, startLine.EndOffset);

        var endCharacter = diagnostic.End.Line == diagnostic.Start.Line ? diagnostic.End.Character : startLine.Length;
        var endOffset = Math.Clamp(startLine.Offset + endCharacter, startOffset, startLine.EndOffset);

        return (startOffset, endOffset);
    }
}
