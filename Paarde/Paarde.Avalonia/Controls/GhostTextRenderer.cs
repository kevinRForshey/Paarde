using Avalonia.Controls;
using Avalonia.Media;

using AvaloniaEdit.Rendering;

namespace Paarde.Avalonia.Controls;

// Draws Epic 15's inline ghost-text suggestion (T15.1.1) right at the caret, dimmed, using an
// InlineObjectElement with DocumentLength 0 - it occupies one visual column but consumes no real
// document text, unlike every other VisualLineElementGenerator in AvaloniaEdit (link/tab/space
// detection) which all replace an existing character. CodeEditor owns the actual suggestion
// state (GhostTextSuggestionState) and calls UpdateSuggestion/Clear here purely to drive what
// this renders, the same split BracketHighlightRenderer/DiffBackgroundRenderer already use between
// pure state and the AvaloniaEdit-specific paint call.
internal sealed class GhostTextRenderer : VisualLineElementGenerator
{
    private readonly TextView _textView;
    private int _offset = -1;
    private string _text = string.Empty;

    public IBrush Foreground { get; set; } = new SolidColorBrush(Color.FromArgb(140, 128, 128, 128));

    public GhostTextRenderer(TextView textView)
    {
        _textView = textView;
        _textView.ElementGenerators.Add(this);
    }

    public void UpdateSuggestion(int offset, string text)
    {
        if (_offset == offset && _text == text)
        {
            return;
        }

        _offset = offset;
        _text = text;
        _textView.Redraw();
    }

    public void Clear()
    {
        if (_offset < 0)
        {
            return;
        }

        _offset = -1;
        _text = string.Empty;
        _textView.Redraw();
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (_offset < 0 || _text.Length == 0)
        {
            return -1;
        }

        var lineEndOffset = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
        return _offset >= startOffset && _offset <= lineEndOffset ? _offset : -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var props = CurrentContext.GlobalTextRunProperties;
        var textBlock = new TextBlock
        {
            Text = _text,
            FontFamily = props.Typeface.FontFamily,
            FontSize = props.FontRenderingEmSize,
            FontStyle = props.Typeface.Style,
            FontWeight = props.Typeface.Weight,
            Foreground = Foreground,
        };

        return new InlineObjectElement(0, textBlock);
    }
}
