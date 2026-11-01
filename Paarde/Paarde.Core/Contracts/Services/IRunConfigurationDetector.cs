using Paarde.Core.Models;

namespace Paarde.Core.Contracts.Services;

// Epic 20 (specs/build-and-run R2): the Run-configuration analogue of IProjectTypeDetector - that
// contract answers "which languages does this working directory contain, and where"; this one
// answers the narrower "which specific project/script in there is something you'd press Run on."
// Language-specific detection knowledge lives entirely inside the implementation as data (file
// classification rules, default command templates), not here, so adding a language later is a
// change to that one class, not a new interface member.
public interface IRunConfigurationDetector
{
    // Keyed the same way IProjectTypeDetector.DetectLanguageRoots returns its result (LanguageId ->
    // root directory). Every returned RunConfiguration.Id is a freshly generated value - merging a
    // candidate against whatever's already persisted (so a user's edits/renames survive repeated
    // detection) is RunConfigurationsProvider's job, not this one's.
    IReadOnlyList<RunConfiguration> DetectConfigurations(IReadOnlyDictionary<string, string> languageRoots);
}
