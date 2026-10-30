using Paarde.Avalonia.Services;
using Paarde.Avalonia.Tests.Fakes;
using Paarde.Avalonia.ViewModels;

namespace Paarde.Avalonia.Tests.ViewModels;

public class QuickOpenViewModelTests
{
    private readonly FakeNavigationService _navigationService = new();
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakeWorkingDirectoryContext _workingDirectoryContext = new();

    private QuickOpenViewModel CreateSut() => new(_navigationService, _localSettingsService, _workingDirectoryContext);

    [Fact]
    public async Task Open_WorkingDirectoryWithFiles_PopulatesResultsAndClearsEmptyState()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;

        try
        {
            File.WriteAllText(Path.Combine(workingDirectory, "Foo.cs"), "class Foo {}");
            File.WriteAllText(Path.Combine(workingDirectory, "Bar.cs"), "class Bar {}");
            await _localSettingsService.SaveSettingAsync(WorkingDirectorySettingsKeys.WorkingDirectory, workingDirectory);

            var sut = CreateSut();
            await Task.Yield();

            sut.OpenCommand.Execute(null);

            Assert.True(sut.IsOpen);
            Assert.Null(sut.EmptyStateMessage);
            Assert.Equal(["Bar.cs", "Foo.cs"], sut.Results);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Open_NoWorkingDirectory_ShowsEmptyStateAndLeavesResultsEmpty()
    {
        var sut = CreateSut();
        await Task.Yield();

        sut.OpenCommand.Execute(null);

        Assert.True(sut.IsOpen);
        Assert.NotNull(sut.EmptyStateMessage);
        Assert.Empty(sut.Results);
    }

    [Fact]
    public async Task QueryChanged_FiltersResultsBySubstring()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;

        try
        {
            File.WriteAllText(Path.Combine(workingDirectory, "Foo.cs"), "class Foo {}");
            File.WriteAllText(Path.Combine(workingDirectory, "Bar.cs"), "class Bar {}");
            await _localSettingsService.SaveSettingAsync(WorkingDirectorySettingsKeys.WorkingDirectory, workingDirectory);

            var sut = CreateSut();
            await Task.Yield();
            sut.OpenCommand.Execute(null);

            sut.Query = "Foo";

            Assert.Equal(["Foo.cs"], sut.Results);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ActivateSelected_NavigatesToExplorerWithAbsolutePathAndCloses()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;

        try
        {
            File.WriteAllText(Path.Combine(workingDirectory, "Foo.cs"), "class Foo {}");
            await _localSettingsService.SaveSettingAsync(WorkingDirectorySettingsKeys.WorkingDirectory, workingDirectory);

            var sut = CreateSut();
            await Task.Yield();
            sut.OpenCommand.Execute(null);
            sut.SelectedResult = "Foo.cs";

            sut.ActivateSelectedCommand.Execute(null);

            Assert.Equal(1, _navigationService.NavigateToCallCount);
            Assert.Equal(typeof(ExplorerViewModel).FullName, _navigationService.LastPageKey);
            Assert.Equal(Path.Combine(workingDirectory, "Foo.cs"), _navigationService.LastParameter);
            Assert.False(sut.IsOpen);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public void ActivateSelected_NoSelectedResult_DoesNotNavigate()
    {
        var sut = CreateSut();

        sut.ActivateSelectedCommand.Execute(null);

        Assert.Equal(0, _navigationService.NavigateToCallCount);
    }

    [Fact]
    public void Close_SetsIsOpenFalse()
    {
        var sut = CreateSut();
        sut.OpenCommand.Execute(null);

        sut.CloseCommand.Execute(null);

        Assert.False(sut.IsOpen);
    }
}
