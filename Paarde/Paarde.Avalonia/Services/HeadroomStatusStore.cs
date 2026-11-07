using FluentAvalonia.UI.Controls;

using Paarde.Avalonia.Contracts.Services;

namespace Paarde.Avalonia.Services;

public class HeadroomStatusStore : IHeadroomStatusStore
{
    public string Message { get; private set; } = string.Empty;

    public InfoBarSeverity Severity { get; private set; } = InfoBarSeverity.Informational;

    public event Action? ResultChanged;

    public void SetResult(string message, InfoBarSeverity severity)
    {
        Message = message;
        Severity = severity;
        ResultChanged?.Invoke();
    }
}
