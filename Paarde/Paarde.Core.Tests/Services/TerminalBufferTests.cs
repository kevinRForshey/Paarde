using System.Text;

using Paarde.Core.Services;

namespace Paarde.Core.Tests.Services;

public class TerminalBufferTests : IDisposable
{
    private readonly TerminalBuffer _buffer = new(cols: 20, rows: 5);

    public void Dispose()
    {
        _buffer.Dispose();
    }

    private static string RowText(TerminalRow row) => string.Concat(row.Cells.Select(c => c.Content));

    [Fact]
    public void Feed_PlainText_AppearsInFirstRow()
    {
        _buffer.Feed(Encoding.UTF8.GetBytes("hello"));

        Assert.StartsWith("hello", RowText(_buffer.GetRow(0)));
    }

    [Fact]
    public void Feed_CursorPositioningEscape_MovesCursor()
    {
        // CSI 5;10H is 1-based (row 5, col 10); Buffer.X/Y are 0-based.
        _buffer.Feed(Encoding.UTF8.GetBytes("\x1b[5;10H"));

        var (row, col, visible) = _buffer.CursorPosition;
        Assert.Equal(4, row);
        Assert.Equal(9, col);
        Assert.True(visible);
    }

    [Fact]
    public void Feed_BasicAnsiColorSequence_ResolvesForegroundToPaletteColor()
    {
        // SGR 31 = basic red. Verified against the real library (XTerm.NET 3.0.0's default
        // palette) that palette index 1 resolves to 0xCD0000, not a theoretical ANSI red.
        _buffer.Feed(Encoding.UTF8.GetBytes("\x1b[31mA\x1b[0m"));

        var cell = _buffer.GetRow(0).Cells[0];
        Assert.Equal("A", cell.Content);
        Assert.Equal(0xCD0000u, cell.Foreground);
    }

    [Fact]
    public void Feed_256ColorAnsiSequence_ResolvesForegroundToPaletteColor()
    {
        // SGR 38;5;196 = 256-color palette index 196, which the default palette resolves to pure
        // red (0xFF0000) - verified against the real library the same way as the basic-color case.
        _buffer.Feed(Encoding.UTF8.GetBytes("\x1b[38;5;196mB\x1b[0m"));

        var cell = _buffer.GetRow(0).Cells[0];
        Assert.Equal("B", cell.Content);
        Assert.Equal(0xFF0000u, cell.Foreground);
    }

    [Fact]
    public void Feed_TrueColorAnsiSequence_ResolvesForegroundToExactRgb()
    {
        _buffer.Feed(Encoding.UTF8.GetBytes("\x1b[38;2;10;20;30mC\x1b[0m"));

        var cell = _buffer.GetRow(0).Cells[0];
        Assert.Equal("C", cell.Content);
        Assert.Equal(0x0A141Eu, cell.Foreground);
    }

    [Fact]
    public void Feed_AlternateBufferSwitch_RestoresNormalBufferContentOnReturn()
    {
        _buffer.Feed(Encoding.UTF8.GetBytes("NORMAL"));
        _buffer.Feed(Encoding.UTF8.GetBytes("\x1b[?1049h")); // enter alternate buffer
        _buffer.Feed(Encoding.UTF8.GetBytes("ALTSCREEN"));

        Assert.StartsWith("ALTSCREEN", RowText(_buffer.GetRow(0)));

        _buffer.Feed(Encoding.UTF8.GetBytes("\x1b[?1049l")); // exit alternate buffer

        Assert.StartsWith("NORMAL", RowText(_buffer.GetRow(0)));
    }

    [Fact]
    public void Resize_UpdatesColsAndRows()
    {
        _buffer.Resize(cols: 40, rows: 10);

        Assert.Equal(40, _buffer.Cols);
        Assert.Equal(10, _buffer.Rows);
    }

    [Fact]
    public void Feed_RaisesChanged()
    {
        var raised = false;
        _buffer.Changed += () => raised = true;

        _buffer.Feed(Encoding.UTF8.GetBytes("x"));

        Assert.True(raised);
    }

    [Fact]
    public void Resize_RaisesChanged()
    {
        var raised = false;
        _buffer.Changed += () => raised = true;

        _buffer.Resize(cols: 30, rows: 8);

        Assert.True(raised);
    }

    [Fact]
    public void Feed_AfterDispose_DoesNotThrow()
    {
        _buffer.Dispose();

        _buffer.Feed(Encoding.UTF8.GetBytes("x"));
    }

    [Fact]
    public void Feed_RealTodoTui1EscapeBlob_DecodesIntoReadableText()
    {
        // The regression test for the bug this epic exists to fix: this is the exact byte
        // sequence a real full-screen TUI app (specs/terminal-emulation/requirements.md's
        // TODOTUI1 repro) sends on its first redraw - alternate-screen entry, SGR color codes,
        // cursor positioning (CSI d / CSI H / CSI X), and box-drawing characters - captured live
        // via IPtyProcessManager against the real repro app and base64-encoded here rather than
        // reconstructed by hand, so the test exercises the actual bytes a real TUI app produces.
        var bytes = Convert.FromBase64String(TodoTui1CaptureBase64);

        using var buffer = new TerminalBuffer(cols: 80, rows: 24);
        buffer.Feed(bytes);

        var screenText = string.Concat(Enumerable.Range(0, buffer.Rows).Select(r => RowText(buffer.GetRow(r))));

        Assert.Contains("Todo TUI (Ctrl+Q to quit)", screenText);
        Assert.Contains("a: add", screenText);
        Assert.Equal(-1, screenText.IndexOf('\x1b'));
    }

    private const string TodoTui1CaptureBase64 =
        "G1s/MWgbPRtbPzEwNDloG1syMjswOzB0G1sxOzI0chsoQhtbbRtbNGwbWz83aBtbPzFsGz4bW0gbWzJKG1s/MjVsG1s/MTJsG1s/MjVoG1s/MW" +
        "gbPRtbPzFoGz0bWz8xMDAzaBtbPzEwMTVoG1s/MTAwNmgbWzM5OzQ5bRtbPzI1bBtbMzk7NDltGyhCG1ttG1tIG1syShtbOTJtG1s0MG0bW0sb" +
        "WzJkG1s5N20bWzQ0beKUjBtbOTZtG1s0NG0gVG9kbyBUVUkgKEN0cmwrUSB0byBxdWl0KSAbWzk3bRtbNDRt4pSA4pSA4pSA4pSA4pSA4pSA4p" +
        "SA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA" +
        "4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSQG1szOzFI4pSCG1szMG0bWzQ3bRtbNzhYG1s4ME" +
        "cbWzk3bRtbNDRt4pSCG1s0OzFI4pSCG1s3OFgbWzgwR+KUghtbNTsxSOKUghtbNzhYG1s4MEfilIIbWzY7MUjilIIbWzc4WBtbODBH4pSCG1s3" +
        "OzFI4pSCG1s3OFgbWzgwR+KUghtbODsxSOKUghtbNzhYG1s4MEfilIIbWzk7MUjilIIbWzc4WBtbODBH4pSCG1sxMDsxSOKUghtbNzhYG1s4ME" +
        "filIIbWzExOzFI4pSCG1s3OFgbWzgwR+KUghtbMTI7MUjilIIbWzc4WBtbODBH4pSCG1sxMzsxSOKUghtbNzhYG1s4MEfilIIbWzE0OzFI4pSC" +
        "G1s3OFgbWzgwR+KUghtbMTU7MUjilIIbWzc4WBtbODBH4pSCG1sxNjsxSOKUghtbNzhYG1s4MEfilIIbWzE3OzFI4pSCG1s3OFgbWzgwR+KUgh" +
        "tbMTg7MUjilIIbWzc4WBtbODBH4pSCG1sxOTsxSOKUghtbNzhYG1s4MEfilIIbWzIwOzFI4pSCG1s3OFgbWzgwR+KUghtbMjE7MUjilIIbWzc4" +
        "WBtbODBH4pSCG1syMjsxSOKUghtbNzhYG1s4MEfilIIbWzIzOzFI4pSCYTogYWRkICAgc3BhY2U6IHRvZ2dsZSBkb25lICAgZDogZGVsZXRlIC" +
        "AgcTogcXVpdBtbMjlYG1s4MEfilIIbWzI0OzFI4pSU4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA" +
        "4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4p" +
        "SA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA4pSA" +
        "4pSA4pSA4pSA4pSA4pSA4pSAG1s/N2zilJgbWz83aBtbM2QIG1szOTs0OW0bKEIbW20=";
}
