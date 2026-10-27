using Paarde.Avalonia.Contracts.Services;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Services;

// Same seam as LanguageServerOptionsProvider: reads Settings fresh on every call rather than
// caching, so a hand-edit made in Settings (R2.6) or a change in the project files on disk takes
// effect the next time this is called.
public class RunConfigurationsProvider
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IProjectTypeDetector _projectTypeDetector;
    private readonly IRunConfigurationDetector _runConfigurationDetector;

    public RunConfigurationsProvider(
        ILocalSettingsService localSettingsService,
        IProjectTypeDetector projectTypeDetector,
        IRunConfigurationDetector runConfigurationDetector)
    {
        _localSettingsService = localSettingsService;
        _projectTypeDetector = projectTypeDetector;
        _runConfigurationDetector = runConfigurationDetector;
    }

    // R2.3: merges a fresh detection pass into whatever's already persisted for this working
    // directory - a candidate never seen before is appended, one the user has since hand-edited
    // (IsAutoDetected: false) is left alone rather than silently overwritten.
    public async Task<RunConfigurationsSnapshot> GetConfigurationsAsync(string workingDirectoryPath)
    {
        var languageRoots = _projectTypeDetector.DetectLanguageRoots(workingDirectoryPath);
        var detected = _runConfigurationDetector.DetectConfigurations(languageRoots);

        var saved = await _localSettingsService.ReadSettingAsync<List<RunConfiguration>>(RunConfigurationSettingsKeys.ForWorkingDirectory(workingDirectoryPath)) ?? [];
        var wasEmpty = saved.Count == 0;

        var merged = MergeDetectedIntoSaved(saved, detected);

        // design.md "Relationship to Epic 12": seeds the very first auto-detected C# candidate's
        // BuildCommand from Epic 12's global BuildCommand (if non-blank) exactly once, when this
        // working directory has never had any configuration saved before; the two settings are
        // allowed to diverge after that.
        if (wasEmpty)
        {
            merged = await SeedFirstCSharpBuildCommandAsync(merged);
        }

        await SaveConfigurationsAsync(workingDirectoryPath, merged);

        var selectedConfigurationId = await _localSettingsService.ReadSettingAsync<string>(RunConfigurationSettingsKeys.SelectedConfigurationId(workingDirectoryPath));

        return new RunConfigurationsSnapshot(merged, selectedConfigurationId);
    }

    public Task SaveConfigurationsAsync(string workingDirectoryPath, IReadOnlyList<RunConfiguration> configurations) =>
        _localSettingsService.SaveSettingAsync(RunConfigurationSettingsKeys.ForWorkingDirectory(workingDirectoryPath), configurations.ToList());

    public Task SaveSelectedConfigurationIdAsync(string workingDirectoryPath, string configurationId) =>
        _localSettingsService.SaveSettingAsync(RunConfigurationSettingsKeys.SelectedConfigurationId(workingDirectoryPath), configurationId);

    private static List<RunConfiguration> MergeDetectedIntoSaved(List<RunConfiguration> saved, IReadOnlyList<RunConfiguration> detected)
    {
        var merged = new List<RunConfiguration>(saved);

        foreach (var candidate in detected)
        {
            var existingIndex = merged.FindIndex(existing => existing.LanguageId == candidate.LanguageId && existing.ProjectRelativePath == candidate.ProjectRelativePath);

            if (existingIndex < 0)
            {
                merged.Add(candidate);
                continue;
            }

            var existing = merged[existingIndex];
            if (!existing.IsAutoDetected)
            {
                // Hand-edited - R2.3 says leave it alone rather than overwrite with fresh detection.
                continue;
            }

            // Keep the persisted Id/Name (a candidate's Id is freshly generated on every detection
            // pass, R2.3), refresh everything detection actually knows about.
            merged[existingIndex] = existing with
            {
                WorkingDirectoryPath = candidate.WorkingDirectoryPath,
                BuildCommand = candidate.BuildCommand,
                RunCommand = candidate.RunCommand,
            };
        }

        return merged;
    }

    private async Task<List<RunConfiguration>> SeedFirstCSharpBuildCommandAsync(List<RunConfiguration> configurations)
    {
        var firstCSharpIndex = configurations.FindIndex(configuration => configuration.LanguageId == "csharp");
        if (firstCSharpIndex < 0)
        {
            return configurations;
        }

        var globalBuildCommand = await _localSettingsService.ReadSettingAsync<string>(BuildTestSettingsKeys.BuildCommand);
        if (string.IsNullOrWhiteSpace(globalBuildCommand))
        {
            return configurations;
        }

        configurations[firstCSharpIndex] = configurations[firstCSharpIndex] with { BuildCommand = globalBuildCommand };
        return configurations;
    }
}

public record RunConfigurationsSnapshot(IReadOnlyList<RunConfiguration> Configurations, string? SelectedConfigurationId);
