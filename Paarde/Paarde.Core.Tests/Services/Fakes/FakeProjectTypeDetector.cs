using Paarde.Core.Contracts.Services;

namespace Paarde.Core.Tests.Services.Fakes;

public class FakeProjectTypeDetector : IProjectTypeDetector
{
    public IReadOnlyDictionary<string, string> LanguageRootsToReturn { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> DetectLanguageRoots(string workingDirectoryPath) => LanguageRootsToReturn;
}
