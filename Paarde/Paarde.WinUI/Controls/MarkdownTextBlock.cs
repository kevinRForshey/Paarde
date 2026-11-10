using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

using Paarde.Core.Helpers;
using Paarde.Core.Models;

using FontStyle = Windows.UI.Text.FontStyle;

namespace Paarde.WinUI.Controls;

// Port of Paarde.Avalonia's MarkdownTextBlock: renders MarkdownInlineParser's output as one wrapped
// TextBlock per source line - indented and bulleted for list items. WinUI's Run has no Background,
// so code spans get theirs from a TextHighlighter over their character range instead.
public sealed partial class MarkdownTextBlock : StackPanel
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarkdownTextBlock), new PropertyMetadata(null, OnRebuildPropertyChanged));

    // StackPanel (unlike TextBlock) has no FontStyle of its own, so callers that want the whole
    // block italicized (e.g. the "further explanation" pane) need this.
    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle), typeof(MarkdownTextBlock), new PropertyMetadata(FontStyle.Normal, OnRebuildPropertyChanged));

    private static readonly SolidColorBrush CodeBackgroundBrush = new(Colors.Gray) { Opacity = 0.3 };
    private static readonly FontFamily CodeFontFamily = new("Cascadia Mono, Consolas");

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public MarkdownTextBlock()
    {
        Spacing = 4;
    }

    private static void OnRebuildPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((MarkdownTextBlock)d).Rebuild();

    private void Rebuild()
    {
        Children.Clear();

        foreach (var line in MarkdownInlineParser.Parse(Text))
        {
            Children.Add(BuildLine(line, FontStyle));
        }
    }

    private static TextBlock BuildLine(MarkdownLine line, FontStyle baseFontStyle)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontStyle = baseFontStyle,
            Margin = line.IsListItem ? new Thickness(16, 0, 0, 0) : default,
        };

        var codeHighlighter = new TextHighlighter { Background = CodeBackgroundBrush };
        var offset = 0;

        if (line.IsListItem)
        {
            const string bullet = "•  ";
            textBlock.Inlines.Add(new Run { Text = bullet, FontStyle = baseFontStyle });
            offset += bullet.Length;
        }

        foreach (var span in line.Spans)
        {
            textBlock.Inlines.Add(BuildRun(span, baseFontStyle));

            if (span.Kind == MarkdownSpanKind.Code)
            {
                codeHighlighter.Ranges.Add(new TextRange(offset, span.Text.Length));
            }

            offset += span.Text.Length;
        }

        if (codeHighlighter.Ranges.Count > 0)
        {
            textBlock.TextHighlighters.Add(codeHighlighter);
        }

        return textBlock;
    }

    private static Run BuildRun(MarkdownSpan span, FontStyle baseFontStyle)
    {
        var run = new Run { Text = span.Text, FontStyle = baseFontStyle };

        switch (span.Kind)
        {
            case MarkdownSpanKind.Bold:
                run.FontWeight = FontWeights.Bold;
                break;
            case MarkdownSpanKind.Italic:
                run.FontStyle = FontStyle.Italic;
                break;
            case MarkdownSpanKind.Code:
                run.FontFamily = CodeFontFamily;
                break;
        }

        return run;
    }
}
