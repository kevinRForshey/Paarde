using Paarde.Presentation.Models;

using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Services;

public class HeadroomStatusStore : IHeadroomStatusStore
{
    public string Message { get; private set; } = string.Empty;

    public NotificationSeverity Severity { get; private set; } = NotificationSeverity.Informational;

    public event Action? ResultChanged;

    public void SetResult(string message, NotificationSeverity severity)
    {
        Message = message;
        Severity = severity;
        ResultChanged?.Invoke();
    }
}
