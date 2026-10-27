using Paarde.Avalonia.Services;
using Paarde.Avalonia.Tests.Fakes;
using Paarde.Avalonia.ViewModels;
using Paarde.Core.Models;
using Paarde.Core.Services;

namespace Paarde.Avalonia.Tests.ViewModels;

public class RunPanelViewModelTests
{
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakeProjectTypeDetector _projectTypeDetector = new();
    private readonly FakeRunConfigurationDetector _runConfigurationDetector = new();
    private readonly FakeBuildTestRunner _buildTestRunner = new();
    private readonly FakeRunProcessManager _runProcessManager = new();
    private readonly FakeWorkingDirectoryContext _workingDirectoryContext = new();
    private readonly RunConfigurationsProvider _runConfigurationsProvider;

    public RunPanelViewModelTests()
    {
        _runConfigurationsProvider = new RunConfigurationsProvider(_localSettingsService, _projectTypeDetector, _runConfigurationDetector);
    }

    private RunPanelViewModel CreateSut() =>
        new(_runConfigurationsProvider, _buildTestRunner, _runProcessManager, _localSettingsService, _workingDirectoryContext);

    private async Task SetSavedWorkingDirectoryAsync(string path)
    {
        await _localSettingsService.SaveSettingAsync(WorkingDirectorySettingsKeys.WorkingDirectory, path);
    }

    private static RunConfiguration MakeConfiguration(string id = "config-1", string buildCommand = "dotnet build", string runCommand = "dotnet run") =>
        new(id, "My App", "csharp", "/repo/src/MyApp", "src/MyApp/MyApp.csproj", buildCommand, runCommand, IsAutoDetected: true);

    [Fact]
    public async Task Construction_WithNoSavedWorkingDirectory_LeavesConfigurationsEmpty()
    {
        var sut = CreateSut();
        await Task.Yield();

        Assert.Empty(sut.Configurations);
        Assert.Null(sut.SelectedConfiguration);
    }

    [Fact]
    public async Task Construction_WithSavedWorkingDirectory_LoadsDetectedConfigurations()
    {
        await SetSavedWorkingDirectoryAsync("/repo");
        _runConfigurationDetector.ConfigurationsToReturn = [MakeConfiguration()];

        var sut = CreateSut();
        await Task.Yield();

        Assert.Single(sut.Configurations);
        Assert.Equal("My App", sut.SelectedConfiguration?.Name);
    }

    [Fact]
    public async Task RunAsync_BuildSucceeds_StartsTheRunCommand()
    {
        await SetSavedWorkingDirectoryAsync("/repo");
        _runConfigurationDetector.ConfigurationsToReturn = [MakeConfiguration()];
        var sut = CreateSut();
        await Task.Yield();
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(0, "build ok", string.Empty, TimeSpan.FromSeconds(1)));

        await sut.RunCommand.ExecuteAsync(null);

        Assert.Single(_runProcessManager.StartCalls);
        Assert.Equal("dotnet run", _runProcessManager.StartCalls[0].Command);
        Assert.Equal("/repo/src/MyApp", _runProcessManager.StartCalls[0].WorkingDirectoryPath);
        Assert.True(sut.IsRunning);
    }

    [Fact]
    public async Task RunAsync_BuildFails_DoesNotStartTheRunCommand()
    {
        await SetSavedWorkingDirectoryAsync("/repo");
        _runConfigurationDetector.ConfigurationsToReturn = [MakeConfiguration()];
        var sut = CreateSut();
        await Task.Yield();
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(1, string.Empty, "build error", TimeSpan.FromSeconds(1)));

        await sut.RunCommand.ExecuteAsync(null);

        Assert.Empty(_runProcessManager.StartCalls);
        Assert.False(sut.IsRunning);
    }

    [Fact]
    public async Task RunAsync_BlankBuildCommand_SkipsBuildAndStartsTheRunCommand()
    {
        await SetSavedWorkingDirectoryAsync("/repo");
        _runConfigurationDetector.ConfigurationsToReturn = [MakeConfiguration(buildCommand: string.Empty)];
        var sut = CreateSut();
        await Task.Yield();

        await sut.RunCommand.ExecuteAsync(null);

        Assert.Equal(0, _buildTestRunner.RunAsyncCallCount);
        Assert.Single(_runProcessManager.StartCalls);
    }

    [Fact]
    public async Task RunAsync_BlankRunCommand_BuildsButDoesNotStart()
    {
        await SetSavedWorkingDirectoryAsync("/repo");
        _runConfigurationDetector.ConfigurationsToReturn = [MakeConfiguration(runCommand: string.Empty)];
        var sut = CreateSut();
        await Task.Yield();
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(0, string.Empty, string.Empty, TimeSpan.FromSeconds(1)));

        await sut.RunCommand.ExecuteAsync(null);

        Assert.Equal(1, _buildTestRunner.RunAsyncCallCount);
        Assert.Empty(_runProcessManager.StartCalls);
    }

    [Fact]
    public async Task BuildAsync_RunsOnlyTheBuildCommand()
    {
        await SetSavedWorkingDirectoryAsync("/repo");
        _runConfigurationDetector.ConfigurationsToReturn = [MakeConfiguration()];
        var sut = CreateSut();
        await Task.Yield();
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(0, "build ok", string.Empty, TimeSpan.FromSeconds(1)));

        await sut.BuildCommand.ExecuteAsync(null);

        Assert.Equal(1, _buildTestRunner.RunAsyncCallCount);
        Assert.Equal("dotnet build", _buildTestRunner.CommandsRun[0]);
        Assert.Empty(_runProcessManager.StartCalls);
    }

    [Fact]
    public async Task StopCommand_WhileRunning_CanExecuteAndStopsTheProcess()
    {
        await SetSavedWorkingDirectoryAsync("/repo");
        _runConfigurationDetector.ConfigurationsToReturn = [MakeConfiguration()];
        var sut = CreateSut();
        await Task.Yield();
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(0, string.Empty, string.Empty, TimeSpan.FromSeconds(1)));
        await sut.RunCommand.ExecuteAsync(null);

        Assert.True(sut.StopCommand.CanExecute(null));
        sut.StopCommand.Execute(null);

        Assert.Equal(1, _runProcessManager.StopCallCount);
    }

    [Fact]
    public async Task StopCommand_WhileNotRunning_CannotExecute()
    {
        await SetSavedWorkingDirectoryAsync("/repo");
        _runConfigurationDetector.ConfigurationsToReturn = [MakeConfiguration()];
        var sut = CreateSut();
        await Task.Yield();

        Assert.False(sut.StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task BuildCommand_NoSelectedConfiguration_CannotExecute()
    {
        var sut = CreateSut();
        await Task.Yield();

        Assert.False(sut.BuildCommand.CanExecute(null));
        Assert.False(sut.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectedConfigurationChanged_PersistsTheSelection()
    {
        await SetSavedWorkingDirectoryAsync("/repo");
        var first = MakeConfiguration("config-1");
        var second = MakeConfiguration("config-2");
        _runConfigurationDetector.ConfigurationsToReturn = [first, second];
        var sut = CreateSut();
        await Task.Yield();

        sut.SelectedConfiguration = second;

        Assert.Equal("config-2", await _localSettingsService.ReadSettingAsync<string>(RunConfigurationSettingsKeys.SelectedConfigurationId("/repo")));
    }

    // RunPanelViewModel.OnProcessOutputReceived/OnProcessExited marshal through
    // Dispatcher.UIThread.Post the same way TerminalViewModel's onOutputLine does (see
    // TerminalViewModelTests' comment on that) - not exercised here since nothing pumps
    // Dispatcher.UIThread in this plain xunit host.
}
