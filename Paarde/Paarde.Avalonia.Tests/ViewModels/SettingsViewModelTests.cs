using FluentAvalonia.UI.Controls;

using Paarde.Avalonia.Models;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.Tests.Fakes;
using Paarde.Avalonia.ViewModels;
using Paarde.Core.Models;
using Paarde.Core.Services;

namespace Paarde.Avalonia.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly FakeThemeSelectorService _themeSelectorService = new();
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakeSecureStorageService _secureStorageService = new();
    private readonly PromptTemplateProvider _promptTemplateProvider = new();
    private readonly LanguageServerOptionsProvider _languageServerOptionsProvider;
    private readonly FakeProjectTypeDetector _projectTypeDetector = new();
    private readonly FakeRunConfigurationDetector _runConfigurationDetector = new();
    private readonly RunConfigurationsProvider _runConfigurationsProvider;
    private readonly FakeHeadroomCoordinator _headroomCoordinator = new();
    private readonly HeadroomStatusStore _headroomStatusStore = new();

    public SettingsViewModelTests()
    {
        _languageServerOptionsProvider = new LanguageServerOptionsProvider(_localSettingsService);
        _runConfigurationsProvider = new RunConfigurationsProvider(_localSettingsService, _projectTypeDetector, _runConfigurationDetector);
    }

    private SettingsViewModel CreateSut() => new(_themeSelectorService, _localSettingsService, _secureStorageService, _promptTemplateProvider, _languageServerOptionsProvider, _runConfigurationsProvider, _headroomCoordinator, _headroomStatusStore);

    [Fact]
    public void ClaudeCliSettings_DefaultToClaudeAndDashP()
    {
        var sut = CreateSut();

        Assert.Equal("claude", sut.ClaudeCliExecutablePath);
        Assert.Equal("-p", sut.ClaudeCliArguments);
    }

    [Fact]
    public async Task ClaudeCliExecutablePath_WhenChanged_PersistsToSettings()
    {
        var sut = CreateSut();
        sut.ClaudeCliExecutablePath = "/usr/local/bin/claude";

        Assert.Equal("/usr/local/bin/claude", await _localSettingsService.ReadSettingAsync<string>("ClaudeCliExecutablePath"));
    }

    [Fact]
    public async Task ClaudeCliArguments_WhenChanged_PersistsToSettings()
    {
        var sut = CreateSut();
        sut.ClaudeCliArguments = "-p --model=opus";

        Assert.Equal("-p --model=opus", await _localSettingsService.ReadSettingAsync<string>("ClaudeCliArguments"));
    }

    [Fact]
    public async Task OnNavigatedTo_RestoresSavedClaudeCliSettings()
    {
        await _localSettingsService.SaveSettingAsync("ClaudeCliExecutablePath", "/opt/claude/claude");
        await _localSettingsService.SaveSettingAsync("ClaudeCliArguments", "-p --verbose");
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.Equal("/opt/claude/claude", sut.ClaudeCliExecutablePath);
        Assert.Equal("-p --verbose", sut.ClaudeCliArguments);
    }

    [Fact]
    public async Task OnNavigatedTo_NoSavedClaudeCliSettings_LeavesDefaults()
    {
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.Equal("claude", sut.ClaudeCliExecutablePath);
        Assert.Equal("-p", sut.ClaudeCliArguments);
    }

    [Fact]
    public void SelectedAnthropicModel_DefaultsToTheMatchingCatalogEntryForTheDefaultModelId()
    {
        var sut = CreateSut();

        Assert.Equal(AnthropicModelCatalog.DefaultModelId, sut.SelectedAnthropicModel?.Id);
    }

    [Fact]
    public void SelectedAnthropicModel_WhenSet_UpdatesAnthropicApiModel()
    {
        var sut = CreateSut();
        var opus = AnthropicModelCatalog.Models.Single(model => model.Id == "claude-opus-5");

        sut.SelectedAnthropicModel = opus;

        Assert.Equal("claude-opus-5", sut.AnthropicApiModel);
    }

    [Fact]
    public async Task OnNavigatedTo_RestoresSavedAnthropicApiModel_UpdatesSelectedAnthropicModelToo()
    {
        await _localSettingsService.SaveSettingAsync("AnthropicApiModel", "claude-opus-5");
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.Equal("claude-opus-5", sut.SelectedAnthropicModel?.Id);
    }

    [Fact]
    public void AgentToolsEnabled_DefaultsToFalse()
    {
        var sut = CreateSut();

        Assert.False(sut.AgentToolsEnabled);
    }

    [Fact]
    public async Task AgentToolsEnabled_WhenChanged_PersistsToSettings()
    {
        var sut = CreateSut();
        sut.AgentToolsEnabled = true;

        Assert.True(await _localSettingsService.ReadSettingAsync<bool?>("ClaudeCliAgentToolsEnabled"));
    }

    [Fact]
    public async Task OnNavigatedTo_RestoresSavedAgentToolsEnabled()
    {
        await _localSettingsService.SaveSettingAsync("ClaudeCliAgentToolsEnabled", true);
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.True(sut.AgentToolsEnabled);
    }

    [Fact]
    public void BuildTestCommands_DefaultToDotnetBuildAndDotnetTest()
    {
        var sut = CreateSut();

        Assert.Equal("dotnet build", sut.BuildCommand);
        Assert.Equal("dotnet test", sut.TestCommand);
    }

    [Fact]
    public async Task BuildCommand_WhenChanged_PersistsToSettings()
    {
        var sut = CreateSut();
        sut.BuildCommand = "msbuild";

        Assert.Equal("msbuild", await _localSettingsService.ReadSettingAsync<string>("BuildTestBuildCommand"));
    }

    [Fact]
    public async Task TestCommand_WhenChanged_PersistsToSettings()
    {
        var sut = CreateSut();
        sut.TestCommand = "npm test";

        Assert.Equal("npm test", await _localSettingsService.ReadSettingAsync<string>("BuildTestTestCommand"));
    }

    [Fact]
    public async Task OnNavigatedTo_RestoresSavedBuildTestCommands()
    {
        await _localSettingsService.SaveSettingAsync("BuildTestBuildCommand", "make");
        await _localSettingsService.SaveSettingAsync("BuildTestTestCommand", "make test");
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.Equal("make", sut.BuildCommand);
        Assert.Equal("make test", sut.TestCommand);
    }

    [Fact]
    public async Task OnNavigatedTo_SavedBuildCommandIsBlank_RestoresTheBlankRatherThanTheDefault()
    {
        await _localSettingsService.SaveSettingAsync("BuildTestBuildCommand", string.Empty);
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.Equal(string.Empty, sut.BuildCommand);
    }

    [Fact]
    public void DefaultPromptTemplate_IsTheProvidersBuiltInTemplate()
    {
        var sut = CreateSut();

        Assert.Equal(_promptTemplateProvider.GetTutorialInstructionPrompt(), sut.DefaultPromptTemplate);
    }

    [Fact]
    public void PromptTemplateOverride_DefaultsToEmpty()
    {
        var sut = CreateSut();

        Assert.Equal(string.Empty, sut.PromptTemplateOverride);
    }

    [Fact]
    public async Task PromptTemplateOverride_WhenChanged_PersistsToSettings()
    {
        var sut = CreateSut();
        sut.PromptTemplateOverride = "Custom instructions.";

        Assert.Equal("Custom instructions.", await _localSettingsService.ReadSettingAsync<string>("PromptTemplateOverride"));
    }

    [Fact]
    public async Task OnNavigatedTo_RestoresSavedPromptTemplateOverride()
    {
        await _localSettingsService.SaveSettingAsync("PromptTemplateOverride", "Custom instructions.");
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.Equal("Custom instructions.", sut.PromptTemplateOverride);
    }

    [Fact]
    public void ResetPromptTemplateCommand_CanExecute_OnlyWhenOverrideIsSet()
    {
        var sut = CreateSut();

        Assert.False(sut.ResetPromptTemplateCommand.CanExecute(null));

        sut.PromptTemplateOverride = "Custom instructions.";

        Assert.True(sut.ResetPromptTemplateCommand.CanExecute(null));
    }

    [Fact]
    public void ResetPromptTemplateCommand_Execute_ClearsTheOverride()
    {
        var sut = CreateSut();
        sut.PromptTemplateOverride = "Custom instructions.";

        sut.ResetPromptTemplateCommand.Execute(null);

        Assert.Equal(string.Empty, sut.PromptTemplateOverride);
    }

    [Fact]
    public void InterfaceMode_DefaultsToEditorFocused()
    {
        var sut = CreateSut();

        Assert.Equal(InterfaceMode.EditorFocused, sut.InterfaceMode);
    }

    [Fact]
    public async Task InterfaceMode_WhenChanged_PersistsToSettings()
    {
        var sut = CreateSut();
        sut.InterfaceMode = InterfaceMode.FlowWizard;

        Assert.Equal(InterfaceMode.FlowWizard, await _localSettingsService.ReadSettingAsync<InterfaceMode?>("Interface.Mode"));
    }

    [Fact]
    public async Task OnNavigatedTo_RestoresSavedInterfaceMode()
    {
        await _localSettingsService.SaveSettingAsync("Interface.Mode", InterfaceMode.FlowWizard);
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.Equal(InterfaceMode.FlowWizard, sut.InterfaceMode);
    }

    [Fact]
    public async Task InterfaceModeChangeRequiresRestart_FalseUntilChangedAwayFromTheLoadedValue()
    {
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.False(sut.InterfaceModeChangeRequiresRestart);

        sut.InterfaceMode = InterfaceMode.FlowWizard;

        Assert.True(sut.InterfaceModeChangeRequiresRestart);

        sut.InterfaceMode = InterfaceMode.EditorFocused;

        Assert.False(sut.InterfaceModeChangeRequiresRestart);
    }

    [Fact]
    public async Task IsHeadroomIntegrationEnabled_DefaultsToFalse()
    {
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.False(sut.IsHeadroomIntegrationEnabled);
    }

    [Fact]
    public async Task IsHeadroomIntegrationEnabled_WhenChanged_PersistsToSettings()
    {
        var sut = CreateSut();

        sut.IsHeadroomIntegrationEnabled = true;

        Assert.True(await _localSettingsService.ReadSettingAsync<bool?>(HeadroomSettingsKeys.IntegrationEnabled));
    }

    [Fact]
    public async Task OnNavigatedTo_RestoresSavedHeadroomIntegrationEnabled()
    {
        await _localSettingsService.SaveSettingAsync(HeadroomSettingsKeys.IntegrationEnabled, true);
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.True(sut.IsHeadroomIntegrationEnabled);
    }

    [Fact]
    public async Task OnNavigatedTo_RestoresHeadroomStatusFromStore()
    {
        _headroomStatusStore.SetResult("Headroom is healthy.", InfoBarSeverity.Success);
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.Equal("Headroom is healthy.", sut.HeadroomStatusMessage);
        Assert.Equal(InfoBarSeverity.Success, sut.HeadroomStatusSeverity);
    }

    [Fact]
    public async Task CheckHeadroomNowCommand_CallsCoordinatorWithSavedWorkingDirectoryAndUpdatesStatus()
    {
        await _localSettingsService.SaveSettingAsync(WorkingDirectorySettingsKeys.WorkingDirectory, "/some/project");
        _headroomCoordinator.MessageToReturn = "Headroom is healthy.";
        _headroomCoordinator.SeverityToReturn = InfoBarSeverity.Success;
        var sut = CreateSut();

        await sut.CheckHeadroomNowCommand.ExecuteAsync(null);

        Assert.Equal(["/some/project"], _headroomCoordinator.ProjectPathsChecked);
        Assert.Equal("Headroom is healthy.", sut.HeadroomStatusMessage);
        Assert.Equal(InfoBarSeverity.Success, sut.HeadroomStatusSeverity);
    }

    [Fact]
    public async Task CheckHeadroomNowCommand_NoWorkingDirectorySaved_ReturnsWarningWithoutCallingCoordinator()
    {
        var sut = CreateSut();

        await sut.CheckHeadroomNowCommand.ExecuteAsync(null);

        Assert.Empty(_headroomCoordinator.ProjectPathsChecked);
        Assert.Equal(InfoBarSeverity.Warning, sut.HeadroomStatusSeverity);
    }
}
