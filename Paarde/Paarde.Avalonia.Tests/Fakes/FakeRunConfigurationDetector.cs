using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Tests.Fakes;

public class FakeRunConfigurationDetector : IRunConfigurationDetector
{
    public IReadOnlyList<RunConfiguration> ConfigurationsToReturn { get; set; } = [];

    public IReadOnlyDictionary<string, string>? LastLanguageRootsReceived { get; private set; }

    public IReadOnlyList<RunConfiguration> DetectConfigurations(IReadOnlyDictionary<string, string> languageRoots)
    {
        LastLanguageRootsReceived = languageRoots;
        return ConfigurationsToReturn;
    }
}
