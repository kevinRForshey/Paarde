using Paarde.Avalonia.Services;
using Paarde.Avalonia.Tests.Fakes;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Tests.Services;

public class RunConfigurationsProviderTests
{
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakeProjectTypeDetector _projectTypeDetector = new();
    private readonly FakeRunConfigurationDetector _runConfigurationDetector = new();

    private const string WorkingDirectoryPath = "/repo";

    private RunConfigurationsProvider CreateSut() => new(_localSettingsService, _projectTypeDetector, _runConfigurationDetector);

    private static RunConfiguration Candidate(string languageId = "csharp", string projectRelativePath = "App.csproj", string buildCommand = "dotnet build", string runCommand = "dotnet run") =>
        new(Guid.NewGuid().ToString(), "App", languageId, WorkingDirectoryPath, projectRelativePath, buildCommand, runCommand, IsAutoDetected: true);

    [Fact]
    public async Task GetConfigurationsAsync_NothingSavedNoneDetected_ReturnsEmpty()
    {
        var sut = CreateSut();

        var snapshot = await sut.GetConfigurationsAsync(WorkingDirectoryPath);

        Assert.Empty(snapshot.Configurations);
        Assert.Null(snapshot.SelectedConfigurationId);
    }

    [Fact]
    public async Task GetConfigurationsAsync_NewCandidateDetected_IsAppended()
    {
        _runConfigurationDetector.ConfigurationsToReturn = [Candidate()];

        var snapshot = await CreateSut().GetConfigurationsAsync(WorkingDirectoryPath);

        var configuration = Assert.Single(snapshot.Configurations);
        Assert.Equal("csharp", configuration.LanguageId);
        Assert.Equal("App.csproj", configuration.ProjectRelativePath);
    }

    [Fact]
    public async Task GetConfigurationsAsync_DetectedAgain_PersistsAcrossCalls()
    {
        _runConfigurationDetector.ConfigurationsToReturn = [Candidate()];
        var sut = CreateSut();

        await sut.GetConfigurationsAsync(WorkingDirectoryPath);
        var secondSnapshot = await sut.GetConfigurationsAsync(WorkingDirectoryPath);

        Assert.Single(secondSnapshot.Configurations);
    }

    [Fact]
    public async Task GetConfigurationsAsync_ReDetectionRefreshesCommandsOfAnAutoDetectedEntry()
    {
        var sut = CreateSut();
        _runConfigurationDetector.ConfigurationsToReturn = [Candidate(buildCommand: "dotnet build v1")];
        var firstSnapshot = await sut.GetConfigurationsAsync(WorkingDirectoryPath);
        var originalId = firstSnapshot.Configurations[0].Id;

        _runConfigurationDetector.ConfigurationsToReturn = [Candidate(buildCommand: "dotnet build v2")];
        var secondSnapshot = await sut.GetConfigurationsAsync(WorkingDirectoryPath);

        var configuration = Assert.Single(secondSnapshot.Configurations);
        Assert.Equal(originalId, configuration.Id);
        Assert.Equal("dotnet build v2", configuration.BuildCommand);
    }

    [Fact]
    public async Task GetConfigurationsAsync_ReDetectionLeavesAHandEditedEntryAlone()
    {
        var sut = CreateSut();
        _runConfigurationDetector.ConfigurationsToReturn = [Candidate(buildCommand: "dotnet build v1")];
        var firstSnapshot = await sut.GetConfigurationsAsync(WorkingDirectoryPath);

        var handEdited = firstSnapshot.Configurations[0] with { BuildCommand = "my custom build", IsAutoDetected = false };
        await sut.SaveConfigurationsAsync(WorkingDirectoryPath, [handEdited]);

        _runConfigurationDetector.ConfigurationsToReturn = [Candidate(buildCommand: "dotnet build v2")];
        var secondSnapshot = await sut.GetConfigurationsAsync(WorkingDirectoryPath);

        var configuration = Assert.Single(secondSnapshot.Configurations);
        Assert.Equal("my custom build", configuration.BuildCommand);
        Assert.False(configuration.IsAutoDetected);
    }

    [Fact]
    public async Task GetConfigurationsAsync_FirstEverCSharpCandidate_SeedsBuildCommandFromEpic12GlobalSetting()
    {
        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.BuildCommand, "dotnet build --configuration Release");
        _runConfigurationDetector.ConfigurationsToReturn = [Candidate(buildCommand: "dotnet build \"App.csproj\"")];

        var snapshot = await CreateSut().GetConfigurationsAsync(WorkingDirectoryPath);

        var configuration = Assert.Single(snapshot.Configurations);
        Assert.Equal("dotnet build --configuration Release", configuration.BuildCommand);
    }

    [Fact]
    public async Task GetConfigurationsAsync_NotTheFirstEverDetectionPass_DoesNotReseedFromEpic12GlobalSetting()
    {
        var sut = CreateSut();
        _runConfigurationDetector.ConfigurationsToReturn = [Candidate(buildCommand: "dotnet build v1")];
        await sut.GetConfigurationsAsync(WorkingDirectoryPath);

        await _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.BuildCommand, "dotnet build --configuration Release");
        _runConfigurationDetector.ConfigurationsToReturn = [Candidate(buildCommand: "dotnet build v2")];
        var snapshot = await sut.GetConfigurationsAsync(WorkingDirectoryPath);

        Assert.Equal("dotnet build v2", snapshot.Configurations[0].BuildCommand);
    }

    [Fact]
    public async Task SaveSelectedConfigurationIdAsync_PersistsPerWorkingDirectory()
    {
        var sut = CreateSut();

        await sut.SaveSelectedConfigurationIdAsync(WorkingDirectoryPath, "abc-123");
        var snapshot = await sut.GetConfigurationsAsync(WorkingDirectoryPath);

        Assert.Equal("abc-123", snapshot.SelectedConfigurationId);
    }

    [Fact]
    public async Task GetConfigurationsAsync_DifferentWorkingDirectories_DoNotShareConfigurations()
    {
        _runConfigurationDetector.ConfigurationsToReturn = [Candidate()];
        var sut = CreateSut();
        await sut.GetConfigurationsAsync(WorkingDirectoryPath);

        _runConfigurationDetector.ConfigurationsToReturn = [];
        var otherSnapshot = await sut.GetConfigurationsAsync("/other-repo");

        Assert.Empty(otherSnapshot.Configurations);
    }
}
