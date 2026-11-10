namespace Paarde.Presentation.Models;

/// <summary>
/// UI-framework-agnostic severity for status/notification bars. Member names and order mirror
/// both FluentAvalonia's and WinUI 3's <c>InfoBarSeverity</c>, so each front end maps it with a
/// plain cast in its own converter.
/// </summary>
public enum NotificationSeverity
{
    Informational,
    Success,
    Warning,
    Error,
}
