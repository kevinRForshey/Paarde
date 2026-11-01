using System;
using System.Globalization;
using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

using Paarde.Core.Services;

namespace Paarde.Avalonia.Controls;

// specs/terminal-emulation, T3: custom-drawn character grid for a TerminalBuffer. Render(DrawingContext)
// rather than a templated control - the same way every terminal emulator draws itself, since a screen's
// worth of individually-styled cells isn't expressible as nested TextBlocks at any reasonable
// performance.
//
// T4 (R4.1/R4.2): keystrokes are encoded here and handed off via InputReceived rather than this
// control taking a direct dependency on IPtyProcessManager - the same "stay ignorant of the
// service, raise an event" shape CodeEditor already uses for GhostTextRequestedEventArgs etc. The
// byte[] shape intentionally matches IPtyProcessManager.WriteInput(byte[]) exactly, so a consumer
// can wire `terminalControl.InputReceived += ptyProcessManager.WriteInput;` directly.
// IPtyProcessManager.WriteInput already no-ops when no process is running, so R4.2's "only while a
// process is running" is satisfied by the consumer's wiring rather than by this control checking
// process state itself.
//
// T5 (R1.3/Goal 5): resizing recomputes cols/rows from the control's pixel size and the measured
// monospace cell, resizes the bound TerminalBuffer directly (it's already held via the Buffer
// property), and raises Resized the same ignorant-of-the-service way InputReceived does, so a
// consumer can wire `control.Resized += ptyProcessManager.Resize;` directly.
public sealed class TerminalControl : Control
{
    // Cascadia Mono/Consolas cover Windows; the three concrete Linux families cover the common
    // distro installs. The trailing generic "monospace" alias is a last resort rather than the
    // only fallback on purpose: Program.cs calls WithInterFont(), so a family that fails to
    // resolve lands on proportional Inter, which silently destroys a fixed-cell grid - every
    // glyph is drawn at a measured-from-"M" cell offset, so non-uniform advances overlap and
    // leave gaps. Naming real families keeps that off the happy path.
    private const string FontFamilyName =
        "Cascadia Mono,Consolas,DejaVu Sans Mono,Liberation Mono,Noto Sans Mono,monospace";

    // Only the control's natural size for a container that measures against infinite space (an
    // Auto grid row, a StackPanel). The grid actually in use always comes from the arranged pixel
    // size via ComputeGridSize, never from these.
    private const int NaturalCols = 80;
    private const int NaturalRows = 24;

    public static readonly StyledProperty<TerminalBuffer?> BufferProperty =
        AvaloniaProperty.Register<TerminalControl, TerminalBuffer?>(nameof(Buffer));

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<TerminalControl, double>(nameof(FontSize), defaultValue: 14);

    private readonly Typeface _typeface = new(FontFamilyName);
    private readonly Typeface _boldTypeface = new(FontFamilyName, weight: FontWeight.Bold);
    private Size _cellSize;

    static TerminalControl()
    {
        BufferProperty.Changed.AddClassHandler<TerminalControl>((control, args) => control.OnBufferChanged(args));
        FontSizeProperty.Changed.AddClassHandler<TerminalControl>((control, _) => control.OnFontSizeChanged());
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

    // The cell grid is measured from this, so changing it re-measures rather than just repainting.
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    private void OnFontSizeChanged()
    {
        _cellSize = MeasureCell();
        InvalidateMeasure();
        InvalidateVisual();
    }

    // Without this a bare Control (no visual children, no template) reports a 0x0 desired size,
    // so an Expander in an Auto grid row - which measures its content against infinite height -
    // arranges this at zero height and ComputeGridSize clamps the buffer to a single row. Fill
    // whatever finite space the container offers; fall back to the natural grid when it offers
    // none.
    protected override Size MeasureOverride(Size availableSize) =>
        ComputeDesiredSize(availableSize, _cellSize);

    // Pure and internal so it's directly unit-testable without a display, the same reasoning as
    // TryEncodeSpecialKey/ComputeGridSize below. Deliberately ignores Buffer's current cols/rows:
    // deriving the desired size from a buffer that OnSizeChanged resizes from the arranged size
    // would close a measure -> resize -> re-measure feedback loop.
    internal static Size ComputeDesiredSize(Size availableSize, Size cellSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? NaturalCols * cellSize.Width : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? NaturalRows * cellSize.Height : availableSize.Height;
        return new Size(width, height);
    }

    // Raw bytes the control wants written to the PTY, already encoded as a real terminal would
    // send them (R4.1).
    public event Action<byte[]>? InputReceived;

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (e.Handled || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        e.Handled = true;
        InputReceived?.Invoke(Encoding.UTF8.GetBytes(e.Text));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled)
        {
            return;
        }

        var bytes = TryEncodeSpecialKey(e.Key, e.KeyModifiers);
        if (bytes is null)
        {
            return;
        }

        e.Handled = true;
        InputReceived?.Invoke(bytes);
    }

    // Pure and internal so it's directly unit-testable without an Avalonia display, per
    // design.md's "Testing approach" ("OnKeyDown's byte-encoding logic is small and pure enough
    // to unit test directly"). Returns null for keys that aren't terminal input at all (e.g.
    // function keys, modifier keys on their own) so OnKeyDown leaves them unhandled.
    internal static byte[]? TryEncodeSpecialKey(Key key, KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Control) && key is >= Key.A and <= Key.Z)
        {
            // Ctrl+<letter> is the letter's 1-based position in the alphabet as a single control
            // byte (Ctrl+A = 0x01 ... Ctrl+Z = 0x1A) - the same mapping every real terminal uses.
            return [(byte)(key - Key.A + 1)];
        }

        return key switch
        {
            Key.Up => Escape("[A"),
            Key.Down => Escape("[B"),
            Key.Right => Escape("[C"),
            Key.Left => Escape("[D"),
            Key.Home => Escape("[H"),
            Key.End => Escape("[F"),
            Key.PageUp => Escape("[5~"),
            Key.PageDown => Escape("[6~"),
            Key.Insert => Escape("[2~"),
            Key.Delete => Escape("[3~"),
            Key.Enter => [(byte)'\r'],
            Key.Tab => [(byte)'\t'],
            Key.Back => [0x7F], // DEL - the backspace byte every modern terminal (incl. xterm) sends.
            Key.Escape => [0x1B],
            _ => null,
        };
    }

    private static byte[] Escape(string suffix) => Encoding.ASCII.GetBytes("\x1b" + suffix);

    // T5 (R1.3/Goal 5): raised after a resize recomputes the grid so a consumer can call
    // IPtyProcessManager.Resize(cols, rows) - same "stay ignorant of the service, raise an event"
    // shape as InputReceived; the signature matches IPtyProcessManager.Resize(int, int) exactly so
    // a consumer can wire `control.Resized += ptyProcessManager.Resize;` directly.
    public event Action<int, int>? Resized;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        var buffer = Buffer;
        if (buffer is null)
        {
            return;
        }

        var (cols, rows) = ComputeGridSize(e.NewSize, _cellSize);
        if (cols == buffer.Cols && rows == buffer.Rows)
        {
            return;
        }

        buffer.Resize(cols, rows);
        Resized?.Invoke(cols, rows);
    }

    // Pure and internal so it's directly unit-testable without an Avalonia display/layout pass,
    // the same reasoning as TryEncodeSpecialKey above.
    internal static (int Cols, int Rows) ComputeGridSize(Size pixelSize, Size cellSize)
    {
        var cols = Math.Max(1, (int)(pixelSize.Width / cellSize.Width));
        var rows = Math.Max(1, (int)(pixelSize.Height / cellSize.Height));
        return (cols, rows);
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
