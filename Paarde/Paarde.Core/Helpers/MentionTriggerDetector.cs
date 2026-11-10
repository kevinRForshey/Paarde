namespace Paarde.Core.Helpers;

// How much of the typed text is an in-progress `@mention` - deliberately plain string/int
// math (no Avalonia TextBox dependency) so PromptView's code-behind can feed it Text/CaretIndex on
// every keystroke and this stays unit-testable on its own.
public readonly record struct MentionTrigger(int TriggerStart, string Query);

public static class MentionTriggerDetector
{
    // Scans backward from the caret for an unescaped '@' that starts the current word - stops (no
    // active mention) the moment it crosses whitespace first, so "email@domain" mid-word and an
    // already-finished "@foo " mention both correctly report no trigger.
    public static MentionTrigger? FindActiveTrigger(string text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text) || caretIndex <= 0 || caretIndex > text.Length)
        {
            return null;
        }

        var index = caretIndex - 1;
        while (index >= 0 && text[index] != '@')
        {
            if (char.IsWhiteSpace(text[index]))
            {
                return null;
            }

            index--;
        }

        if (index < 0 || (index > 0 && !char.IsWhiteSpace(text[index - 1])))
        {
            return null;
        }

        return new MentionTrigger(index, text[(index + 1)..caretIndex]);
    }
}
