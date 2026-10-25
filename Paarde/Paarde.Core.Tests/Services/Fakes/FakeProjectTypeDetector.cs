using Paarde.Core.Contracts.Services;

namespace Paarde.Core.Tests.Services.Fakes;

public class FakeProjectTypeDetector : IProjectTypeDetector
{
    public IReadOnlySet<string> LanguageIdsToReturn { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlySet<string> DetectLanguageIds(string workingDirectoryPath) => LanguageIdsToReturn;
}
