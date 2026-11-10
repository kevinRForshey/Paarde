using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

using Paarde.Avalonia.Helpers;
using Paarde.Core.Models;
using Paarde.Presentation.Helpers;

namespace Paarde.Avalonia.Controls;

// Renders one diff line's text as plain runs, combining two independent
// tokenizations of the same LineText - DiffBuilder.BuildWordDiff's word-level diff (which tokens
// actually changed against this line's Removed/Added partner, given a background highlight) and
// SyntaxTokenizer's TextMate grammar tokens (what color each token's syntax gets, as a foreground)
// - since neither alone should suppress the other: an unchanged keyword still needs its syntax
// color, and a changed-but-unrecognized token still needs its diff highlight. WordSpans is null for
// an Unchanged line, a side-by-side padding cell, or a change that never paired with a partner to
// diff against; FilePath null/unrecognized falls back to no syntax coloring. Either fallback still
// renders LineText correctly, just without that one layer.
public class DiffWordSpanTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> LineTextProperty =
        AvaloniaProperty.Register<DiffWordSpanTextBlock, string?>(nameof(LineText));

    public static readonly StyledProperty<IReadOnlyList<DiffLine>?> WordSpansProperty =
        AvaloniaProperty.Register<DiffWordSpanTextBlock, IReadOnlyList<DiffLine>?>(nameof(WordSpans));

    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<DiffWordSpanTextBlock, string?>(nameof(FilePath));

    public string? LineText
    {
        get => GetValue(LineTextProperty);
        set => SetValue(LineTextProperty, value);
    }

    public IReadOnlyList<DiffLine>? WordSpans
    {
        get => GetValue(WordSpansProperty);
        set => SetValue(WordSpansProperty, value);
    }

    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    // Same hue as DiffLineKindToBackgroundConverter's Added/Removed row backgrounds, but a much
    // higher alpha - a changed word needs to stand out against a row that's already tinted.
    private static readonly SolidColorBrush AddedWordBrush = new(Color.FromArgb(110, 46, 160, 67));
    private static readonly SolidColorBrush RemovedWordBrush = new(Color.FromArgb(110, 218, 54, 51));

    static DiffWordSpanTextBlock()
    {
        LineTextProperty.Changed.AddClassHandler<DiffWordSpanTextBlock>((control, _) => control.Rebuild());
        WordSpansProperty.Changed.AddClassHandler<DiffWordSpanTextBlock>((control, _) => control.Rebuild());
        FilePathProperty.Changed.AddClassHandler<DiffWordSpanTextBlock>((control, _) => control.Rebuild());
    }

    private void Rebuild()
    {
        Inlines!.Clear();

        if (string.IsNullOrEmpty(LineText))
        {
            return;
        }

        var syntaxTokens = SyntaxTokenizer.Tokenize(FilePath, LineText);

        foreach (var span in DiffLineRenderer.Build(LineText, WordSpans, syntaxTokens))
        {
            var run = new Run(span.Text)
            {
                Background = span.Kind switch
                {
                    DiffLineKind.Added => AddedWordBrush,
                    DiffLineKind.Removed => RemovedWordBrush,
                    _ => null,
                },
            };

            if (span.Color is not null && Color.TryParse(span.Color, out var parsedColor))
            {
                run.Foreground = new SolidColorBrush(parsedColor);
            }

            Inlines.Add(run);
        }
    }
}
