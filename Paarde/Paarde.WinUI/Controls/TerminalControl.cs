using System.Text;

using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

using Paarde.Core.Services;

using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI;
using Windows.UI.Core;

namespace Paarde.WinUI.Controls;

// Port of Paarde.Avalonia's TerminalControl (specs/terminal-emulation T3-T5): a character grid for a
// TerminalBuffer. Avalonia draws every cell in Render(DrawingContext); WinUI has no immediate-mode
// drawing without Win2D, whose NuGet package is under Microsoft's Win2D license rather than MIT,
// so this lays the grid out with plain XAML elements on a Canvas instead:
// - one Rectangle per run of same-background cells,
// - one TextBlock per run of same-style text, placed at its exact column, so a monospace font can't
//   drift out of the grid by more than the run itself (wide characters always get a run of their
//   own),
// - rows whose cells haven't changed since the last refresh are left alone.
//
// Same "stay ignorant of the service, raise an event" shape as Avalonia's: InputReceived/Resized
// match IPtyProcessManager.WriteInput/Resize, so a consumer wires them straight through.
public sealed partial class TerminalControl : UserControl
{
    // Only the control's natural size for a container that measures against infinite space (an
    // Expander's content, a StackPanel). The grid actually in use always comes from the arranged
    // size via ComputeGridSize.
    private const int NaturalCols = 80;
    private const int NaturalRows = 24;

    public static readonly DependencyProperty BufferProperty = DependencyProperty.Register(
        nameof(Buffer), typeof(TerminalBuffer), typeof(TerminalControl), new PropertyMetadata(null, (d, e) => ((TerminalControl)d).OnBufferChanged(e)));

    private static readonly SolidColorBrush FocusedCursorBrush = new(Color.FromArgb(128, 255, 255, 255));
    private static readonly SolidColorBrush UnfocusedCursorBrush = new(Colors.White);

    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private readonly Rectangle _cursor = new() { IsHitTestVisible = false };
    private readonly List<RenderedRow> _renderedRows = [];
    private readonly Dictionary<uint, SolidColorBrush> _brushes = [];
    private readonly DispatcherQueue _dispatcherQueue;

    private Size _cellSize;
    private int _refreshPending;
    private bool _isLoaded;
    private char? _pendingHighSurrogate;

    public TerminalControl()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        IsTabStop = true;
        UseSystemFocusVisuals = false;
        IsTextScaleFactorEnabled = false;
        FontFamily = (FontFamily)Application.Current.Resources["MonospaceFontFamily"];
        FontSize = 14;

        Canvas.SetZIndex(_cursor, 1);
        _canvas.Children.Add(_cursor);
        Content = new Grid { Background = new SolidColorBrush(Colors.Transparent), Children = { _canvas } };

        _cellSize = MeasureCell();
        RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => OnFontChanged());
        RegisterPropertyChangedCallback(FontFamilyProperty, (_, _) => OnFontChanged());

        SizeChanged += (_, e) => OnSizeChanged(e.NewSize);
        GotFocus += (_, _) => UpdateCursor();
        LostFocus += (_, _) => UpdateCursor();
        PointerPressed += (_, _) => Focus(FocusState.Pointer);
        CharacterReceived += OnCharacterReceived;

        // Buffers are long-lived singletons while this control is recreated with its page, so the
        // Changed subscription only lives while the control is in the tree.
        Loaded += (_, _) =>
        {
            _isLoaded = true;
            Subscribe(Buffer);

            // The device-pixel snap in MeasureCell needs XamlRoot, which only exists from here on
            // (and changes when the window moves to a monitor with a different scale).
            OnFontChanged();
            XamlRoot.Changed += OnXamlRootChanged;
        };
        Unloaded += (_, _) =>
        {
            _isLoaded = false;
            Unsubscribe(Buffer);

            if (XamlRoot is not null)
            {
                XamlRoot.Changed -= OnXamlRootChanged;
            }
        };
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        var cellSize = MeasureCell();
        if (cellSize != _cellSize)
        {
            OnFontChanged();
        }
    }

    public TerminalBuffer? Buffer
    {
        get => (TerminalBuffer?)GetValue(BufferProperty);
        set => SetValue(BufferProperty, value);
    }

    // Raw bytes the control wants written to the PTY, already encoded as a real terminal would
    // send them.
    public event Action<byte[]>? InputReceived;

    // Raised after a resize recomputes the grid so a consumer can call IPtyProcessManager.Resize.
    public event Action<int, int>? Resized;

    protected override Size MeasureOverride(Size availableSize)
    {
        _canvas.Measure(availableSize);
        return ComputeDesiredSize(availableSize, _cellSize);
    }

    // Same as Avalonia's: fill whatever finite space the container offers, fall back to the natural
    // grid when it offers none, and never derive it from the buffer's own size (that would close a
    // measure -> resize -> re-measure loop).
    internal static Size ComputeDesiredSize(Size availableSize, Size cellSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? NaturalCols * cellSize.Width : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? NaturalRows * cellSize.Height : availableSize.Height;
        return new Size(width, height);
    }

    internal static (int Cols, int Rows) ComputeGridSize(Size pixelSize, Size cellSize)
    {
        var cols = Math.Max(1, (int)(pixelSize.Width / cellSize.Width));
        var rows = Math.Max(1, (int)(pixelSize.Height / cellSize.Height));
        return (cols, rows);
    }

    // PreviewKeyDown rather than KeyDown so Tab and the arrow keys reach the PTY instead of moving
    // focus.
    protected override void OnPreviewKeyDown(KeyRoutedEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // AltGr arrives as Ctrl+Alt; leave it to CharacterReceived so e.g. AltGr+Q still types '@'
        // on a German layout instead of sending Ctrl+Q.
        var control = IsKeyDown(VirtualKey.Control) && !IsKeyDown(VirtualKey.Menu);
        var bytes = TryEncodeSpecialKey(e.Key, control);
        if (bytes is null)
        {
            return;
        }

        e.Handled = true;
        InputReceived?.Invoke(bytes);
    }

    // Same mapping as Avalonia's TryEncodeSpecialKey. Returns null for keys that aren't terminal
    // input on their own (function keys, bare modifiers, printable keys - those come through
    // CharacterReceived).
    internal static byte[]? TryEncodeSpecialKey(VirtualKey key, bool control)
    {
        if (control && key is >= VirtualKey.A and <= VirtualKey.Z)
        {
            // Ctrl+A = 0x01 ... Ctrl+Z = 0x1A, the mapping every real terminal uses.
            return [(byte)(key - VirtualKey.A + 1)];
        }

        return key switch
        {
            VirtualKey.Up => Escape("[A"),
            VirtualKey.Down => Escape("[B"),
            VirtualKey.Right => Escape("[C"),
            VirtualKey.Left => Escape("[D"),
            VirtualKey.Home => Escape("[H"),
            VirtualKey.End => Escape("[F"),
            VirtualKey.PageUp => Escape("[5~"),
            VirtualKey.PageDown => Escape("[6~"),
            VirtualKey.Insert => Escape("[2~"),
            VirtualKey.Delete => Escape("[3~"),
            VirtualKey.Enter => [(byte)'\r'],
            VirtualKey.Tab => [(byte)'\t'],
            VirtualKey.Back => [0x7F], // DEL - the backspace byte every modern terminal (incl. xterm) sends.
            VirtualKey.Escape => [0x1B],
            _ => null,
        };
    }

    private static byte[] Escape(string suffix) => Encoding.ASCII.GetBytes("\x1b" + suffix);

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        var character = e.Character;

        // Control characters (Enter, Tab, Backspace, Escape, Ctrl+letter) are already sent by
        // OnPreviewKeyDown.
        if (character < ' ' || character == '\x7f')
        {
            return;
        }

        e.Handled = true;

        // Characters outside the BMP arrive as two separate surrogate events.
        if (char.IsHighSurrogate(character))
        {
            _pendingHighSurrogate = character;
            return;
        }

        var text = _pendingHighSurrogate is { } high && char.IsLowSurrogate(character)
            ? new string([high, character])
            : character.ToString();
        _pendingHighSurrogate = null;

        InputReceived?.Invoke(Encoding.UTF8.GetBytes(text));
    }

    private void OnSizeChanged(Size newSize)
    {
        _canvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, newSize.Width, newSize.Height) };

        var buffer = Buffer;
        if (buffer is null || newSize.Width <= 0 || newSize.Height <= 0)
        {
            return;
        }

        var (cols, rows) = ComputeGridSize(newSize, _cellSize);
        if (cols == buffer.Cols && rows == buffer.Rows)
        {
            return;
        }

        buffer.Resize(cols, rows);
        Resized?.Invoke(cols, rows);
    }

    private void OnFontChanged()
    {
        _cellSize = MeasureCell();
        ClearRenderedRows();
        InvalidateMeasure();

        // The control's own size may not change, so SizeChanged won't re-fit the grid by itself.
        if (ActualWidth > 0)
        {
            OnSizeChanged(new Size(ActualWidth, ActualHeight));
        }

        RequestRefresh();
    }

    // Averaged over a run of glyphs so layout rounding of a single glyph's width doesn't
    // accumulate across a whole row.
    private Size MeasureCell()
    {
        const int probeLength = 20;
        var probe = new TextBlock
        {
            Text = new string('M', probeLength),
            FontFamily = FontFamily,
            FontSize = FontSize,
            IsTextScaleFactorEnabled = false,
            UseLayoutRounding = false,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // Row height snapped to whole device pixels: a fractional row top anti-aliases the edges of
        // each row's background rectangles, which shows as a faint seam between rows.
        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        var height = Math.Ceiling(probe.DesiredSize.Height * scale) / scale;
        return new Size(probe.DesiredSize.Width / probeLength, height);
    }

    private void OnBufferChanged(DependencyPropertyChangedEventArgs e)
    {
        if (_isLoaded)
        {
            Unsubscribe(e.OldValue as TerminalBuffer);
            Subscribe(e.NewValue as TerminalBuffer);
        }

        ClearRenderedRows();
        RequestRefresh();
    }

    private void Subscribe(TerminalBuffer? buffer)
    {
        if (buffer is not null)
        {
            buffer.Changed += RequestRefresh;
        }
    }

    private void Unsubscribe(TerminalBuffer? buffer)
    {
        if (buffer is not null)
        {
            buffer.Changed -= RequestRefresh;
        }
    }

    // TerminalBuffer.Changed fires on the PTY reader thread, once per chunk of output; coalesce
    // them into one refresh per UI-thread turn.
    private void RequestRefresh()
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) == 0)
        {
            _dispatcherQueue.TryEnqueue(Refresh);
        }
    }

    private void Refresh()
    {
        Interlocked.Exchange(ref _refreshPending, 0);

        var buffer = Buffer;
        if (buffer is null)
        {
            ClearRenderedRows();
            UpdateCursor();
            return;
        }

        for (var row = 0; row < buffer.Rows; row++)
        {
            var cells = buffer.GetRow(row).Cells;

            if (row < _renderedRows.Count)
            {
                if (_renderedRows[row].Cells.AsSpan().SequenceEqual(cells))
                {
                    continue;
                }

                RemoveElements(_renderedRows[row]);
                _renderedRows[row] = RenderRow(cells, row);
            }
            else
            {
                _renderedRows.Add(RenderRow(cells, row));
            }
        }

        while (_renderedRows.Count > buffer.Rows)
        {
            RemoveElements(_renderedRows[^1]);
            _renderedRows.RemoveAt(_renderedRows.Count - 1);
        }

        UpdateCursor();
    }

    private RenderedRow RenderRow(TerminalCell[] cells, int rowIndex)
    {
        var rendered = new RenderedRow(cells);
        var y = rowIndex * _cellSize.Height;

        // Backgrounds: one rectangle per run of the same color.
        var col = 0;
        var i = 0;
        while (i < cells.Length)
        {
            var background = BackgroundOf(cells[i]);
            var startCol = col;

            while (i < cells.Length && BackgroundOf(cells[i]) == background)
            {
                col += cells[i].Width;
                i++;
            }

            var rectangle = new Rectangle
            {
                Width = (col - startCol) * _cellSize.Width,
                Height = _cellSize.Height,
                Fill = GetBrush(background),
            };
            Canvas.SetLeft(rectangle, startCol * _cellSize.Width);
            Canvas.SetTop(rectangle, y);
            AddElement(rendered, rectangle);
        }

        // Text: one TextBlock per run of same-style single-width cells (blanks ride along in any
        // run), and one per wide character.
        col = 0;
        i = 0;
        var text = new StringBuilder();
        while (i < cells.Length)
        {
            var cell = cells[i];

            if (cell.Width == 0 || IsBlank(cell))
            {
                col += cell.Width;
                i++;
                continue;
            }

            var startCol = col;
            text.Clear();

            if (cell.Width > 1)
            {
                text.Append(cell.Content);
                col += cell.Width;
                i++;
            }
            else
            {
                while (i < cells.Length && cells[i].Width == 1 && (IsBlank(cells[i]) || HasSameTextStyle(cells[i], cell)))
                {
                    text.Append(IsBlank(cells[i]) ? " " : cells[i].Content);
                    col++;
                    i++;
                }
            }

            var textBlock = new TextBlock
            {
                Text = text.ToString().TrimEnd(),
                FontFamily = FontFamily,
                FontSize = FontSize,
                FontWeight = cell.Bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = GetBrush(ForegroundOf(cell)),
                IsTextScaleFactorEnabled = false,
                TextWrapping = TextWrapping.NoWrap,
            };
            Canvas.SetLeft(textBlock, startCol * _cellSize.Width);
            Canvas.SetTop(textBlock, y);
            AddElement(rendered, textBlock);
        }

        return rendered;
    }

    private static bool IsBlank(TerminalCell cell) => cell.Invisible || string.IsNullOrEmpty(cell.Content) || cell.Content == " ";

    private static uint BackgroundOf(TerminalCell cell) => cell.Inverse ? cell.Foreground : cell.Background;

    private static uint ForegroundOf(TerminalCell cell) => cell.Inverse ? cell.Background : cell.Foreground;

    private static bool HasSameTextStyle(TerminalCell a, TerminalCell b) => ForegroundOf(a) == ForegroundOf(b) && a.Bold == b.Bold;

    private SolidColorBrush GetBrush(uint packedRgb)
    {
        if (!_brushes.TryGetValue(packedRgb, out var brush))
        {
            brush = new SolidColorBrush(Color.FromArgb(255, (byte)(packedRgb >> 16), (byte)(packedRgb >> 8), (byte)packedRgb));
            _brushes[packedRgb] = brush;
        }

        return brush;
    }

    private void AddElement(RenderedRow row, UIElement element)
    {
        row.Elements.Add(element);
        _canvas.Children.Add(element);
    }

    private void RemoveElements(RenderedRow row)
    {
        foreach (var element in row.Elements)
        {
            _canvas.Children.Remove(element);
        }
    }

    private void ClearRenderedRows()
    {
        foreach (var row in _renderedRows)
        {
            RemoveElements(row);
        }

        _renderedRows.Clear();
    }

    // Filled while focused, outlined otherwise - same as Avalonia's.
    private void UpdateCursor()
    {
        if (Buffer is not { } buffer)
        {
            _cursor.Visibility = Visibility.Collapsed;
            return;
        }

        var (row, col, visible) = buffer.CursorPosition;
        _cursor.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _cursor.Width = _cellSize.Width;
        _cursor.Height = _cellSize.Height;
        Canvas.SetLeft(_cursor, col * _cellSize.Width);
        Canvas.SetTop(_cursor, row * _cellSize.Height);

        var isFocused = FocusState != FocusState.Unfocused;
        _cursor.Fill = isFocused ? FocusedCursorBrush : null;
        _cursor.Stroke = isFocused ? null : UnfocusedCursorBrush;
        _cursor.StrokeThickness = isFocused ? 0 : 1;
    }

    private sealed class RenderedRow(TerminalCell[] cells)
    {
        public TerminalCell[] Cells { get; } = cells;

        public List<UIElement> Elements { get; } = [];
    }
}
