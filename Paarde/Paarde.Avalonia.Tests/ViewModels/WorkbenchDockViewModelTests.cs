using Paarde.Avalonia.Services;
using Paarde.Avalonia.Tests.Fakes;
using Paarde.Avalonia.ViewModels;
using Paarde.Core.Models;
using Paarde.Core.ViewModels;

namespace Paarde.Avalonia.Tests.ViewModels;

public class WorkbenchDockViewModelTests
{
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakeDiagnosticsAggregator _diagnosticsAggregator = new();

    private readonly FakeProjectTypeDetector _projectTypeDetector = new();
    private readonly FakeRunConfigurationDetector _runConfigurationDetector = new();
    private readonly FakeBuildTestRunner _buildTestRunner = new();
    private readonly FakePtyProcessManager _runPtyProcessManager = new();
    private readonly FakeWorkingDirectoryContext _runWorkingDirectoryContext = new();
    private readonly RunPanelViewModel _runPanelViewModel;

    private readonly FakePtyProcessManager _terminalPtyProcessManager = new();
    private readonly TerminalViewModel _terminalViewModel;

    public WorkbenchDockViewModelTests()
    {
        var runConfigurationsProvider = new RunConfigurationsProvider(_localSettingsService, _projectTypeDetector, _runConfigurationDetector);
        _runPanelViewModel = new RunPanelViewModel(runConfigurationsProvider, _buildTestRunner, _runPtyProcessManager, _localSettingsService, _runWorkingDirectoryContext);
        _terminalViewModel = new TerminalViewModel(_terminalPtyProcessManager, _localSettingsService);
    }

    private WorkbenchDockViewModel CreateSut() =>
        new(_runPanelViewModel, _terminalViewModel, _diagnosticsAggregator, _localSettingsService);

    private static RunConfiguration MakeConfiguration() =>
        new("config-1", "My App", "csharp", "/repo/src/MyApp", "src/MyApp/MyApp.csproj", "dotnet build", "dotnet run", IsAutoDetected: true);

    [Fact]
    public async Task Construction_NoSavedState_DefaultsToRunCollapsed()
    {
        var sut = CreateSut();
        await Task.Yield();

        Assert.Equal("Run", sut.ActiveTabKey);
        Assert.False(sut.IsExpanded);
    }

    [Fact]
    public async Task Construction_SavedState_RestoresActiveTabAndIsExpanded()
    {
        await _localSettingsService.SaveSettingAsync(WorkbenchDockSettingsKeys.ActiveTab, "Problems");
        await _localSettingsService.SaveSettingAsync(WorkbenchDockSettingsKeys.IsExpanded, true);

        var sut = CreateSut();
        await Task.Yield();

        Assert.Equal("Problems", sut.ActiveTabKey);
        Assert.True(sut.IsExpanded);
    }

    [Fact]
    public async Task ActiveTabKeyChanged_PersistsToSettings()
    {
        var sut = CreateSut();

        sut.ActiveTabKey = "Terminal";

        Assert.Equal("Terminal", await _localSettingsService.ReadSettingAsync<string>(WorkbenchDockSettingsKeys.ActiveTab));
    }

    [Fact]
    public async Task IsExpandedChanged_PersistsToSettings()
    {
        var sut = CreateSut();

        sut.IsExpanded = true;

        Assert.True(await _localSettingsService.ReadSettingAsync<bool?>(WorkbenchDockSettingsKeys.IsExpanded));
    }

    [Fact]
    public void RunPanelStartsBuilding_ExpandsAndSwitchesToBuildTab()
    {
        var sut = CreateSut();

        _runPanelViewModel.SelectedConfiguration = MakeConfiguration();
        _ = _runPanelViewModel.BuildCommand.ExecuteAsync(null);

        Assert.True(sut.IsExpanded);
        Assert.Equal("Build", sut.ActiveTabKey);
    }

    [Fact]
    public async Task RunPanelStartsRunning_ExpandsAndSwitchesToRunTab()
    {
        var sut = CreateSut();
        _runPanelViewModel.SelectedConfiguration = MakeConfiguration();

        await _runPanelViewModel.RunCommand.ExecuteAsync(null);

        Assert.True(sut.IsExpanded);
        Assert.Equal("Run", sut.ActiveTabKey);
    }
}
