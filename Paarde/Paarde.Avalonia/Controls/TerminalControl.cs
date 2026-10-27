using System;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using Paarde.Core.Services;

namespace Paarde.Avalonia.Controls;

// specs/terminal-emulation, T3: custom-drawn character grid for a TerminalBuffer. Render(DrawingContext)
// rather than a templated control - the same way every terminal emulator draws itself, since a screen's
// worth of individually-styled cells isn't expressible as nested TextBlocks at any reasonable
// performance. Input handling (T4) and resize-driven cols/rows recomputation (T5) land in later tasks.
public sealed class TerminalControl : Control
{
    private const string FontFamilyName = "Cascadia Mono,Consolas,monospace";
    private const double FontSize = 13;

    public static readonly StyledProperty<TerminalBuffer?> BufferProperty =
        AvaloniaProperty.Register<TerminalControl, TerminalBuffer?>(nameof(Buffer));

    private readonly Typeface _typeface = new(FontFamilyName);
    private readonly Typeface _boldTypeface = new(FontFamilyName, weight: FontWeight.Bold);
    private readonly Size _cellSize;

    static TerminalControl()
    {
        BufferProperty.Changed.AddClassHandler<TerminalControl>((control, args) => control.OnBufferChanged(args));
        AffectsRender<TerminalControl>(BufferProperty);
    }

    public TerminalControl()
    {
        Focusable = true;
        _cellSize = MeasureCell();
    }

    public TerminalBuffer? Buffer
    {
        get => GetValue(BufferProperty);
        set => SetValue(BufferProperty, value);
    }

    private Size MeasureCell()
    {
        var probe = new FormattedText(
            "M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, FontSize, Brushes.White);
        return new Size(probe.WidthIncludingTrailingWhitespace, probe.Height);
    }

    private void OnBufferChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (args.OldValue is TerminalBuffer oldBuffer)
        {
            oldBuffer.Changed -= OnTerminalBufferChanged;
        }

        if (args.NewValue is TerminalBuffer newBuffer)
        {
            newBuffer.Changed += OnTerminalBufferChanged;
        }

        InvalidateVisual();
    }

    private void OnTerminalBufferChanged()
    {
        Dispatcher.UIThread.Post(InvalidateVisual);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var buffer = Buffer;
        if (buffer is null)
        {
            return;
        }

        for (var row = 0; row < buffer.Rows; row++)
        {
            RenderRow(context, buffer.GetRow(row), row);
        }

        RenderCursor(context, buffer);
    }

    private void RenderRow(DrawingContext context, TerminalRow row, int rowIndex)
    {
        var x = 0.0;
        var y = rowIndex * _cellSize.Height;

        foreach (var cell in row.Cells)
        {
            var cellWidth = _cellSize.Width * cell.Width;
            var backgroundColor = cell.Inverse ? cell.Foreground : cell.Background;
            context.FillRectangle(ToBrush(backgroundColor), new Rect(x, y, cellWidth, _cellSize.Height));

            if (!cell.Invisible && cell.Content is { Length: > 0 } and not " ")
            {
                var foregroundColor = cell.Inverse ? cell.Background : cell.Foreground;
                var typeface = cell.Bold ? _boldTypeface : _typeface;
                var formattedText = new FormattedText(
                    cell.Content, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, FontSize,
                    ToBrush(foregroundColor));
                context.DrawText(formattedText, new Point(x, y));
            }

            x += cellWidth;
        }
    }

    private void RenderCursor(DrawingContext context, TerminalBuffer buffer)
    {
        var (row, col, visible) = buffer.CursorPosition;
        if (!visible)
        {
            return;
        }

        var rect = new Rect(col * _cellSize.Width, row * _cellSize.Height, _cellSize.Width, _cellSize.Height);

        if (IsFocused)
        {
            context.FillRectangle(new SolidColorBrush(Colors.White, 0.5), rect);
        }
        else
        {
            context.DrawRectangle(new Pen(Brushes.White), rect, 0f);
        }
    }

    private static IBrush ToBrush(uint packedRgb) =>
        new SolidColorBrush(Color.FromRgb((byte)(packedRgb >> 16), (byte)(packedRgb >> 8), (byte)packedRgb));
}
