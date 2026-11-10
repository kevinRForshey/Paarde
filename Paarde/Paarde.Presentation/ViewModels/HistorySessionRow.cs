using Paarde.Presentation.Strings;
using Paarde.Core.Models;

namespace Paarde.Presentation.ViewModels;

// Display wrapper around a persisted RunSession for the History page's master list.
// Pre-formats DisplayText in C# (From), following TutorialViewModel.FormatHistoryEntry's precedent,
// rather than using XAML StringFormat (unused elsewhere in this codebase) or putting UI-specific
// formatting (truncation, locale-dependent date formatting) on the Core RunSession model itself.
public record HistorySessionRow(RunSession Session, string DisplayText)
{
    private const int PromptExcerptMaxLength = 60;

    public static HistorySessionRow From(RunSession session)
    {
        var timestamp = session.StartedAt.ToLocalTime().ToString("g");
        var displayText = string.Format("History_SessionRow".Resolve(), timestamp, Excerpt(session.UserPromptText), session.FileCount);

        return new HistorySessionRow(session, displayText);
    }

    private static string Excerpt(string userPromptText)
    {
        var singleLine = userPromptText.ReplaceLineEndings(" ").Trim();

        return singleLine.Length > PromptExcerptMaxLength
            ? singleLine[..PromptExcerptMaxLength] + "…"
            : singleLine;
    }
}
