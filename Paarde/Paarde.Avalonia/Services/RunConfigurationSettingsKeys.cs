namespace Paarde.Avalonia.Services;

// specs/build-and-run/design.md: RunConfiguration itself and IRunConfigurationDetector live in
// Paarde.Core since they have no settings dependency, but these keys (and RunConfigurationsProvider,
// which reads/writes them) live here instead, next to LanguageServerSettingsKeys/
// BuildTestSettingsKeys/WorkingDirectorySettingsKeys - ILocalSettingsService is declared in
// Paarde.Avalonia.Contracts.Services, which Paarde.Core has no reference to.
//
// Keyed per working directory (R1.2: multiple configurations and one "selected" choice persist
// per working directory, not globally) by folding the path into the key itself, since
// ILocalSettingsService exposes one flat key/value dictionary rather than a nested per-directory
// store.
public static class RunConfigurationSettingsKeys
{
    public static string ForWorkingDirectory(string workingDirectoryPath) => $"RunConfigurationsCollection::{workingDirectoryPath}";

    public static string SelectedConfigurationId(string workingDirectoryPath) => $"RunConfigurationsSelectedId::{workingDirectoryPath}";
}
