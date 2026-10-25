using Paarde.Avalonia.Helpers;

namespace Paarde.Avalonia.Tests.Helpers;

public class GhostTextSuggestionStateTests
{
    [Fact]
    public void HasSuggestion_InitialState_IsFalse()
    {
        var state = new GhostTextSuggestionState();

        Assert.False(state.HasSuggestion);
    }

    [Fact]
    public void Show_SetsOffsetTextAndHasSuggestion()
    {
        var state = new GhostTextSuggestionState();

        state.Show(10, "var x = 1;");

        Assert.True(state.HasSuggestion);
        Assert.Equal(10, state.Offset);
        Assert.Equal("var x = 1;", state.Text);
    }

    [Fact]
    public void Show_EmptyText_LeavesHasSuggestionFalse()
    {
        var state = new GhostTextSuggestionState();

        state.Show(10, "");

        Assert.False(state.HasSuggestion);
    }

    [Fact]
    public void Accept_CaretAtSuggestionOffset_ReturnsTextAndDismisses()
    {
        var state = new GhostTextSuggestionState();
        state.Show(10, "var x = 1;");

        var accepted = state.Accept(10);

        Assert.Equal("var x = 1;", accepted);
        Assert.False(state.HasSuggestion);
    }

    [Fact]
    public void Accept_CaretElsewhere_ReturnsNullAndLeavesSuggestionInPlace()
    {
        var state = new GhostTextSuggestionState();
        state.Show(10, "var x = 1;");

        var accepted = state.Accept(5);

        Assert.Null(accepted);
        Assert.True(state.HasSuggestion);
    }

    [Fact]
    public void Accept_NoSuggestion_ReturnsNull()
    {
        var state = new GhostTextSuggestionState();

        Assert.Null(state.Accept(0));
    }

    [Fact]
    public void Dismiss_ClearsOffsetAndText()
    {
        var state = new GhostTextSuggestionState();
        state.Show(10, "var x = 1;");

        state.Dismiss();

        Assert.False(state.HasSuggestion);
        Assert.Equal(-1, state.Offset);
        Assert.Equal(string.Empty, state.Text);
    }
}
