using FluentAvalonia.UI.Controls;

using Paarde.Avalonia.Contracts.Services;

namespace Paarde.Avalonia.Tests.Fakes;

public class FakeHeadroomCoordinator : IHeadroomCoordinator
{
    public string MessageToReturn { get; set; } = string.Empty;

    public InfoBarSeverity SeverityToReturn { get; set; } = InfoBarSeverity.Informational;

    public List<string> ProjectPathsChecked { get; } = new();

    public Task<(string Message, InfoBarSeverity Severity)> CheckAndOfferFixesAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        ProjectPathsChecked.Add(projectPath);
        return Task.FromResult((MessageToReturn, SeverityToReturn));
    }
}
