using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

using Paarde.Core.Models;
using Paarde.Presentation.Helpers;
using Paarde.WinUI.Helpers;

using Windows.UI;

namespace Paarde.WinUI.Controls;

// Port of Paarde.Avalonia's DiffWordSpanTextBlock (T8.3.3/T8.3.4): one diff line's text, combining
// DiffBuilder's word-level diff (changed tokens get a background) with SyntaxTokenizer's TextMate
// tokens (syntax-colored foreground). WinUI's TextBlock is sealed and its Run has no Background, so
// this wraps a TextBlock and draws the changed-word backgrounds with TextHighlighters.
public sealed partial class DiffWordSpanTextBlock : UserControl
{
    public static readonly DependencyProperty LineTextProperty =
        DependencyProperty.Register(nameof(LineText), typeof(string), typeof(DiffWordSpanTextBlock), new PropertyMetadata(null, OnRebuildPropertyChanged));

    public static readonly DependencyProperty WordSpansProperty =
        DependencyProperty.Register(nameof(WordSpans), typeof(IReadOnlyList<DiffLine>), typeof(DiffWordSpanTextBlock), new PropertyMetadata(null, OnRebuildPropertyChanged));

    public static readonly DependencyProperty FilePathProperty =
        DependencyProperty.Register(nameof(FilePath), typeof(string), typeof(DiffWordSpanTextBlock), new PropertyMetadata(null, OnRebuildPropertyChanged));

    // Same hue as the Added/Removed row backgrounds, but a much higher alpha - a changed word needs
    // to stand out against a row that's already tinted.
    private static readonly SolidColorBrush AddedWordBrush = new(Color.FromArgb(110, 46, 160, 67));
    private static readonly SolidColorBrush RemovedWordBrush = new(Color.FromArgb(110, 218, 54, 51));

    private readonly TextBlock _textBlock = new() { TextWrapping = TextWrapping.NoWrap };

    public string? LineText
    {
        get => (string?)GetValue(LineTextProperty);
        set => SetValue(LineTextProperty, value);
    }

    public IReadOnlyList<DiffLine>? WordSpans
    {
        get => (IReadOnlyList<DiffLine>?)GetValue(WordSpansProperty);
        set => SetValue(WordSpansProperty, value);
    }

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public DiffWordSpanTextBlock()
    {
        Content = _textBlock;
    }

    private static void OnRebuildPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DiffWordSpanTextBlock)d).Rebuild();

    private void Rebuild()
    {
        _textBlock.Inlines.Clear();
        _textBlock.TextHighlighters.Clear();

        if (string.IsNullOrEmpty(LineText))
        {
            return;
        }

        var addedHighlighter = new TextHighlighter { Background = AddedWordBrush };
        var removedHighlighter = new TextHighlighter { Background = RemovedWordBrush };
        var syntaxTokens = SyntaxTokenizer.Tokenize(FilePath, LineText);
        var offset = 0;

        foreach (var span in DiffLineRenderer.Build(LineText, WordSpans, syntaxTokens))
        {
            var run = new Run { Text = span.Text };

            if (ColorParser.TryParse(span.Color, out var parsedColor))
            {
                run.Foreground = new SolidColorBrush(parsedColor);
            }

            switch (span.Kind)
            {
                case DiffLineKind.Added:
                    addedHighlighter.Ranges.Add(new TextRange(offset, span.Text.Length));
                    break;
                case DiffLineKind.Removed:
                    removedHighlighter.Ranges.Add(new TextRange(offset, span.Text.Length));
                    break;
            }

            _textBlock.Inlines.Add(run);
            offset += span.Text.Length;
        }

        if (addedHighlighter.Ranges.Count > 0)
        {
            _textBlock.TextHighlighters.Add(addedHighlighter);
        }

        if (removedHighlighter.Ranges.Count > 0)
        {
            _textBlock.TextHighlighters.Add(removedHighlighter);
        }
    }
}
