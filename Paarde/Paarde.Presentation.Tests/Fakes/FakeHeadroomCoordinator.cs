using Paarde.Presentation.Models;

using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeHeadroomCoordinator : IHeadroomCoordinator
{
    public string MessageToReturn { get; set; } = string.Empty;

    public NotificationSeverity SeverityToReturn { get; set; } = NotificationSeverity.Informational;

    public List<string> ProjectPathsChecked { get; } = new();

    public Task<(string Message, NotificationSeverity Severity)> CheckAndOfferFixesAsync(string projectPath, bool isAutomaticCheck, CancellationToken cancellationToken = default)
    {
        ProjectPathsChecked.Add(projectPath);
        return Task.FromResult((MessageToReturn, SeverityToReturn));
    }
}
