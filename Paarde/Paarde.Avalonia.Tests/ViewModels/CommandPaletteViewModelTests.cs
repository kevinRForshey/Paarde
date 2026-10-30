using Paarde.Avalonia.Services;
using Paarde.Avalonia.Tests.Fakes;
using Paarde.Avalonia.ViewModels;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Tests.ViewModels;

public class CommandPaletteViewModelTests
{
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakeProjectTypeDetector _projectTypeDetector = new();
    private readonly FakeRunConfigurationDetector _runConfigurationDetector = new();
    private readonly FakeBuildTestRunner _buildTestRunner = new();
    private readonly FakePtyProcessManager _ptyProcessManager = new();
    private readonly FakeWorkingDirectoryContext _runPanelWorkingDirectoryContext = new();
    private readonly FakeNavigationService _navigationService = new();
    private readonly RunPanelViewModel _runPanelViewModel;

    public CommandPaletteViewModelTests()
    {
        var runConfigurationsProvider = new RunConfigurationsProvider(_localSettingsService, _projectTypeDetector, _runConfigurationDetector);
        _runPanelViewModel = new RunPanelViewModel(runConfigurationsProvider, _buildTestRunner, _ptyProcessManager, _localSettingsService, _runPanelWorkingDirectoryContext);
    }

    private CommandPaletteViewModel CreateSut() => new(_runPanelViewModel, _navigationService);

    private static RunConfiguration MakeConfiguration(string name = "My App") =>
        new("config-1", name, "csharp", "/repo/src/MyApp", "src/MyApp/MyApp.csproj", "dotnet build", "dotnet run", IsAutoDetected: true);

    [Fact]
    public void Open_PopulatesResultsWithFullRegistry()
    {
        var sut = CreateSut();

        sut.OpenCommand.Execute(null);

        Assert.True(sut.IsOpen);
        Assert.Contains(sut.Results, c => c.Name == "Build");
        Assert.Contains(sut.Results, c => c.Name == "Run");
        Assert.Contains(sut.Results, c => c.Name == "Stop");
        Assert.Contains(sut.Results, c => c.Name == "Go to Explorer");
        Assert.Equal(11, sut.Results.Count);
    }

    [Fact]
    public void QueryChanged_FiltersRegistryByNameSubstring_CaseInsensitive()
    {
        var sut = CreateSut();
        sut.OpenCommand.Execute(null);

        sut.Query = "go to ex";

        Assert.Equal(["Go to Explorer"], sut.Results.Select(c => c.Name));
    }

    [Fact]
    public void Open_StopCommand_IsDisabledWhenNothingIsRunning()
    {
        var sut = CreateSut();
        sut.OpenCommand.Execute(null);

        var stop = Assert.Single(sut.Results, c => c.Name == "Stop");

        Assert.False(stop.IsEnabled);
    }

    [Fact]
    public void Open_BuildCommand_IsEnabledWhenAConfigurationIsSelected()
    {
        _runPanelViewModel.SelectedConfiguration = MakeConfiguration();
        var sut = CreateSut();
        sut.OpenCommand.Execute(null);

        var build = Assert.Single(sut.Results, c => c.Name == "Build");

        Assert.True(build.IsEnabled);
    }

    [Fact]
    public void ExecuteSelected_GoToEntry_NavigatesAndCloses()
    {
        var sut = CreateSut();
        sut.OpenCommand.Execute(null);
        sut.SelectedResult = sut.Results.Single(c => c.Name == "Go to Explorer");

        sut.ExecuteSelectedCommand.Execute(null);

        Assert.Equal(typeof(ExplorerViewModel).FullName, _navigationService.LastPageKey);
        Assert.False(sut.IsOpen);
    }

    [Fact]
    public void ExecuteSelected_DisabledCommand_DoesNotExecuteButStillCloses()
    {
        var sut = CreateSut();
        sut.OpenCommand.Execute(null);
        sut.SelectedResult = sut.Results.Single(c => c.Name == "Stop");

        sut.ExecuteSelectedCommand.Execute(null);

        Assert.Equal(0, _ptyProcessManager.StopCallCount);
        Assert.False(sut.IsOpen);
    }

    [Fact]
    public void Close_SetsIsOpenFalse()
    {
        var sut = CreateSut();
        sut.OpenCommand.Execute(null);

        sut.CloseCommand.Execute(null);

        Assert.False(sut.IsOpen);
    }

    [Fact]
    public void Open_WhenQuickOpenIsOpen_ClosesQuickOpen()
    {
        var quickOpenViewModel = new QuickOpenViewModel(_navigationService, _localSettingsService, _runPanelWorkingDirectoryContext);
        var sut = CreateSut();
        sut.AttachQuickOpen(quickOpenViewModel);
        quickOpenViewModel.OpenCommand.Execute(null);

        sut.OpenCommand.Execute(null);

        Assert.False(quickOpenViewModel.IsOpen);
        Assert.True(sut.IsOpen);
    }
}
