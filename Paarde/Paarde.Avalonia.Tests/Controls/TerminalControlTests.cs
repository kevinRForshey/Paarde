using System.Text;

using Avalonia;
using Avalonia.Input;

using Paarde.Avalonia.Controls;

namespace Paarde.Avalonia.Tests.Controls;

public class TerminalControlTests
{
    [Theory]
    [InlineData(Key.Up, "\x1b[A")]
    [InlineData(Key.Down, "\x1b[B")]
    [InlineData(Key.Right, "\x1b[C")]
    [InlineData(Key.Left, "\x1b[D")]
    [InlineData(Key.Home, "\x1b[H")]
    [InlineData(Key.End, "\x1b[F")]
    [InlineData(Key.PageUp, "\x1b[5~")]
    [InlineData(Key.PageDown, "\x1b[6~")]
    [InlineData(Key.Insert, "\x1b[2~")]
    [InlineData(Key.Delete, "\x1b[3~")]
    [InlineData(Key.Enter, "\r")]
    [InlineData(Key.Tab, "\t")]
    [InlineData(Key.Escape, "\x1b")]
    public void TryEncodeSpecialKey_RecognizedKeyWithNoModifiers_ReturnsExpectedBytes(Key key, string expected)
    {
        var bytes = TerminalControl.TryEncodeSpecialKey(key, KeyModifiers.None);

        Assert.Equal(Encoding.ASCII.GetBytes(expected), bytes);
    }

    [Fact]
    public void TryEncodeSpecialKey_Backspace_ReturnsDelByte()
    {
        var bytes = TerminalControl.TryEncodeSpecialKey(Key.Back, KeyModifiers.None);

        Assert.Equal(new byte[] { 0x7F }, bytes);
    }

    [Theory]
    [InlineData(Key.A, 0x01)]
    [InlineData(Key.C, 0x03)]
    [InlineData(Key.Z, 0x1A)]
    public void TryEncodeSpecialKey_CtrlLetter_ReturnsControlByte(Key key, byte expected)
    {
        var bytes = TerminalControl.TryEncodeSpecialKey(key, KeyModifiers.Control);

        Assert.Equal(new[] { expected }, bytes);
    }

    [Fact]
    public void TryEncodeSpecialKey_LetterWithoutControlModifier_ReturnsNull()
    {
        var bytes = TerminalControl.TryEncodeSpecialKey(Key.A, KeyModifiers.None);

        Assert.Null(bytes);
    }

    [Fact]
    public void TryEncodeSpecialKey_UnrecognizedKey_ReturnsNull()
    {
        var bytes = TerminalControl.TryEncodeSpecialKey(Key.F5, KeyModifiers.None);

        Assert.Null(bytes);
    }

    [Fact]
    public void ComputeGridSize_ExactMultipleOfCellSize_ReturnsExactCols()
    {
        var (cols, rows) = TerminalControl.ComputeGridSize(new Size(800, 500), new Size(10, 20));

        Assert.Equal(80, cols);
        Assert.Equal(25, rows);
    }

    [Fact]
    public void ComputeGridSize_PartialTrailingCell_RoundsDown()
    {
        // 805px / 10px cells = 80 full cells + 5px of a partial 81st - truncated, not rounded up,
        // the same "no half-drawn trailing cell" behavior a real terminal emulator wants.
        var (cols, rows) = TerminalControl.ComputeGridSize(new Size(805, 509), new Size(10, 20));

        Assert.Equal(80, cols);
        Assert.Equal(25, rows);
    }

    [Fact]
    public void ComputeGridSize_SmallerThanOneCell_ClampsToOne()
    {
        var (cols, rows) = TerminalControl.ComputeGridSize(new Size(5, 5), new Size(10, 20));

        Assert.Equal(1, cols);
        Assert.Equal(1, rows);
    }

    [Fact]
    public void ComputeDesiredSize_FiniteAvailableSize_FillsAllOfIt()
    {
        var desired = TerminalControl.ComputeDesiredSize(new Size(640, 360), new Size(10, 20));

        Assert.Equal(new Size(640, 360), desired);
    }

    [Fact]
    public void ComputeDesiredSize_InfiniteHeight_FallsBackToTheNatural24RowHeight()
    {
        // The regression this guards: a bare Control reports 0x0, so an Expander in an Auto grid
        // row (which measures its content against infinite height) arranged the terminal at zero
        // height and ComputeGridSize clamped the buffer to one unreadable row.
        var desired = TerminalControl.ComputeDesiredSize(
            new Size(640, double.PositiveInfinity), new Size(10, 20));

        Assert.Equal(640, desired.Width);
        Assert.Equal(24 * 20, desired.Height);
    }

    [Fact]
    public void ComputeDesiredSize_InfiniteInBothDirections_FallsBackToTheNatural80x24Grid()
    {
        var desired = TerminalControl.ComputeDesiredSize(
            new Size(double.PositiveInfinity, double.PositiveInfinity), new Size(10, 20));

        Assert.Equal(80 * 10, desired.Width);
        Assert.Equal(24 * 20, desired.Height);
    }
}
