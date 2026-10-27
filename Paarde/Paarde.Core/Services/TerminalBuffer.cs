using XTerm;
using XTerm.Buffer;
using XTerm.Options;

namespace Paarde.Core.Services;

// specs/terminal-emulation T2: a single cell's resolved, renderer-ready state. Colors are
// resolved to packed 0xRRGGBB here (see TerminalBuffer.ResolveColor) rather than left as raw
// XTerm.NET mode/index pairs, so Paarde.Avalonia's rendering control (T3) never needs to
// reference XTerm.NET types.
public readonly record struct TerminalCell(
    string Content,
    int Width,
    uint Foreground,
    uint Background,
    bool Bold,
    bool Dim,
    bool Italic,
    bool Underline,
    bool Blink,
    bool Inverse,
    bool Invisible,
    bool Strikethrough,
    bool Overline);

public sealed class TerminalRow
{
    public TerminalRow(TerminalCell[] cells)
    {
        Cells = cells;
    }

    // Wide-character continuation cells (Width == 0) are already dropped by TerminalBuffer.GetRow,
    // so this is exactly the sequence of cells a renderer should draw left to right.
    public TerminalCell[] Cells { get; }
}

// specs/terminal-emulation T2: wraps an XTerm.NET Terminal. No VT100/ANSI parsing logic of its
// own - Feed hands raw bytes straight to the library and GetRow/CursorPosition read back the
// state it already computed. The one piece of real logic here is ResolveColor, because
// XTerm.NET's own cell attributes carry a raw mode/index pair rather than a renderer-ready color
// (see its remarks below).
public sealed class TerminalBuffer : IDisposable
{
    private readonly Terminal _terminal;

    public TerminalBuffer(int cols, int rows)
    {
        _terminal = new Terminal(new TerminalOptions { Cols = cols, Rows = rows });
    }

    // XTerm.NET's own BufferChanged event only fires on alternate/normal screen switches, not on
    // every write (confirmed against its source - Terminal.cs raises it exclusively from
    // SwitchToAltBuffer/SwitchToNormalBuffer). There's no library event that means "something was
    // painted, go redraw", so TerminalBuffer raises its own after every Feed/Resize instead of
    // trying to track dirty regions itself.
    public event Action? Changed;

    public int Cols => _terminal.Cols;

    public int Rows => _terminal.Rows;

    public (int Row, int Col, bool Visible) CursorPosition =>
        (_terminal.Buffer.Y, _terminal.Buffer.X, _terminal.CursorVisible);

    public void Feed(byte[] bytes)
    {
        _terminal.Write(bytes);
        Changed?.Invoke();
    }

    public void Resize(int cols, int rows)
    {
        _terminal.Resize(cols, rows);
        Changed?.Invoke();
    }

    public TerminalRow GetRow(int rowIndex)
    {
        var buffer = _terminal.Buffer;
        var line = buffer.Lines[buffer.YDisp + rowIndex];

        // A row below everything written so far - same null check XTerm.NET's own README
        // rendering example makes before indexing into the line.
        if (line is null)
        {
            return new TerminalRow(Array.Empty<TerminalCell>());
        }

        var cells = new List<TerminalCell>(_terminal.Cols);

        for (var col = 0; col < _terminal.Cols; col++)
        {
            var cell = line[col];

            // Second cell of a wide character (CJK, emoji, ...) - already represented by the
            // preceding cell, same as XTerm.NET's own README rendering example skips it.
            if (cell.Width == 0)
            {
                continue;
            }

            cells.Add(ToTerminalCell(cell));
        }

        return new TerminalRow(cells.ToArray());
    }

    private TerminalCell ToTerminalCell(BufferCell cell)
    {
        var attributes = cell.Attributes;

        return new TerminalCell(
            cell.Content,
            cell.Width,
            ResolveColor(attributes.GetFgColorMode(), attributes.GetFgColor(), isForeground: true),
            ResolveColor(attributes.GetBgColorMode(), attributes.GetBgColor(), isForeground: false),
            attributes.IsBold(),
            attributes.IsDim(),
            attributes.IsItalic(),
            attributes.IsUnderline(),
            attributes.IsBlink(),
            attributes.IsInverse(),
            attributes.IsInvisible(),
            attributes.IsStrikethrough(),
            attributes.IsOverline());
    }

    // AttributeData packs color as (mode << 25) | index, per its own source
    // (Buffer/AttributeData.cs: GetFgColor/GetFgColorMode). Mode 0 holds a 0-255 palette index -
    // basic SGR 30-37/90-97 colors and 256-color SGR 38;5 both land here - except for the sentinel
    // values AttributeData.Default uses for "no color set" (256 for foreground, 257 for
    // background), which mean "use the terminal's own default", not palette index 256. Mode 1
    // holds a direct RGB value (SGR 38;2). This contract was verified empirically against the
    // real library (XTerm.NET 3.0.0) rather than taken from its README, whose documented "0 =
    // default / 1 = 256-color / 2 = RGB" comment does not match what the source actually does.
    private uint ResolveColor(int mode, int color, bool isForeground)
    {
        if (mode == 1)
        {
            return (uint)color;
        }

        if (isForeground && color == 256)
        {
            return (uint)_terminal.Colors.Foreground;
        }

        if (!isForeground && color == 257)
        {
            return (uint)_terminal.Colors.Background;
        }

        return (uint)_terminal.Colors[color];
    }

    public void Dispose()
    {
        _terminal.Dispose();
    }
}
