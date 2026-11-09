using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeHeadroomService : IHeadroomService
{
    public bool IsInstalledResult { get; set; } = true;

    public bool IsUvAvailableResult { get; set; } = true;

    public HeadroomHealthResult HealthResult { get; set; } = new(0, []);

    public bool IsProjectWiredResult { get; set; } = true;

    public CommandResult InstallResult { get; set; } = new(0, string.Empty, string.Empty, TimeSpan.Zero);

    public CommandResult RunInitClaudeResult { get; set; } = new(0, string.Empty, string.Empty, TimeSpan.Zero);

    public int InstallCallCount { get; private set; }

    public List<string> InitClaudeProjectPaths { get; } = new();

    public bool IsInstalled() => IsInstalledResult;

    public bool IsUvAvailable() => IsUvAvailableResult;

    public Task<HeadroomHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(HealthResult);

    public Task<bool> IsProjectWiredAsync(string projectPath) => Task.FromResult(IsProjectWiredResult);

    public Task<CommandResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        InstallCallCount++;
        return Task.FromResult(InstallResult);
    }

    public Task<CommandResult> RunInitClaudeAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        InitClaudeProjectPaths.Add(projectPath);
        return Task.FromResult(RunInitClaudeResult);
    }
}
