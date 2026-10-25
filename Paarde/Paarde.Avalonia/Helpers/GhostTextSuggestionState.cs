namespace Paarde.Avalonia.Helpers;

// Pure accept/dismiss state machine behind Epic 15's ghost-text suggestion (T15.1.1/T15.3.1) - same
// split as BracketHighlightRenderer's internal state vs. its pure FindMatchingPair, so the
// transitions here are unit-testable without a real AvaloniaEdit TextArea. A suggestion is only ever
// valid at the exact document offset it was shown at; any edit that moves the caret away from that
// offset makes it stale, which is why Accept takes the current caret offset rather than trusting the
// caller.
public sealed class GhostTextSuggestionState
{
    public int Offset { get; private set; } = -1;

    public string Text { get; private set; } = string.Empty;

    public bool HasSuggestion => Offset >= 0 && Text.Length > 0;

    public void Show(int offset, string text)
    {
        Offset = offset;
        Text = text;
    }

    // Returns the text to insert if a suggestion is actually showing at exactly this caret offset,
    // clearing it in the process; null if there's nothing to accept (caller should fall back to
    // whatever Tab normally does, e.g. indent).
    public string? Accept(int caretOffset)
    {
        if (!HasSuggestion || caretOffset != Offset)
        {
            return null;
        }

        var text = Text;
        Dismiss();
        return text;
    }

    public void Dismiss()
    {
        Offset = -1;
        Text = string.Empty;
    }
}
