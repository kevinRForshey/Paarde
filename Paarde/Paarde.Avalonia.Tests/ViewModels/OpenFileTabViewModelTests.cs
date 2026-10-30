using Paarde.Avalonia.Tests.Fakes;
using Paarde.Avalonia.ViewModels;

namespace Paarde.Avalonia.Tests.ViewModels;

// Epic 23, T23.1.1: Save/Revert/Explain used to live on ExplorerViewModel against a single
// SelectedFilePath/SelectedFileContent slot - these tests moved here along with that logic onto
// OpenFileTabViewModel, one instance per open Explorer tab.
public class OpenFileTabViewModelTests : IDisposable
{
    private readonly FakeFileChangeApplyService _fileChangeApplyService = new();
    private readonly FakeLlmRunner _llmRunner = new();
    private readonly FakeLanguageServerSessionManager _languageServerSessionManager = new();
    private readonly string _workingDirectory;

    public OpenFileTabViewModelTests()
    {
        _workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    private OpenFileTabViewModel CreateSut(string relativeFilePath, string initialContent) =>
        new(Path.Combine(_workingDirectory, relativeFilePath), initialContent, _workingDirectory, _fileChangeApplyService, _llmRunner, _languageServerSessionManager);

    [Fact]
    public void Content_Edited_MarksDirty()
    {
        var sut = CreateSut("a.txt", "original");

        sut.Content = "edited";

        Assert.True(sut.IsDirty);
    }

    [Fact]
    public void Content_EditedBackToOriginal_ClearsDirty()
    {
        var sut = CreateSut("a.txt", "original");
        sut.Content = "edited";

        sut.Content = "original";

        Assert.False(sut.IsDirty);
    }

    [Fact]
    public void SaveCommand_NotDirty_CannotExecute()
    {
        var sut = CreateSut("a.txt", "original");

        Assert.False(sut.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void SaveCommand_Dirty_Succeeds_WritesAndClearsDirty()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        var sut = CreateSut("a.txt", "original");
        sut.Content = "edited";

        Assert.True(sut.SaveCommand.CanExecute(null));
        sut.SaveCommand.Execute(null);

        Assert.False(sut.IsDirty);
        Assert.Equal(1, _fileChangeApplyService.WriteRawCallCount);
        Assert.Equal("a.txt", _fileChangeApplyService.LastWriteRawFilePath);
        Assert.Equal("edited", _fileChangeApplyService.LastWriteRawContent);
        Assert.NotEmpty(sut.StatusMessage);
    }

    [Fact]
    public void SaveCommand_ProtectedPath_IsBlockedAndDoesNotWrite()
    {
        Directory.CreateDirectory(Path.Combine(_workingDirectory, "bin"));
        File.WriteAllText(Path.Combine(_workingDirectory, "bin", "a.txt"), "original");
        var sut = CreateSut(Path.Combine("bin", "a.txt"), "original");
        sut.Content = "edited";

        sut.SaveCommand.Execute(null);

        Assert.Equal(0, _fileChangeApplyService.WriteRawCallCount);
        Assert.True(sut.IsDirty);
        Assert.NotEmpty(sut.StatusMessage);
    }

    [Fact]
    public void SaveCommand_FileChangedOnDiskSinceOpened_IsBlockedAndDoesNotWrite()
    {
        var filePath = Path.Combine(_workingDirectory, "a.txt");
        File.WriteAllText(filePath, "original");
        var sut = CreateSut("a.txt", "original");
        sut.Content = "edited";
        File.WriteAllText(filePath, "changed externally");

        sut.SaveCommand.Execute(null);

        Assert.Equal(0, _fileChangeApplyService.WriteRawCallCount);
        Assert.True(sut.IsDirty);
        Assert.NotEmpty(sut.StatusMessage);
    }

    [Fact]
    public void RevertToDiskCommand_NotDirty_CannotExecute()
    {
        var sut = CreateSut("a.txt", "original");

        Assert.False(sut.RevertToDiskCommand.CanExecute(null));
    }

    [Fact]
    public void RevertToDiskCommand_Dirty_ReloadsFromDiskAndClearsDirty()
    {
        var filePath = Path.Combine(_workingDirectory, "a.txt");
        File.WriteAllText(filePath, "original");
        var sut = CreateSut("a.txt", "original");
        sut.Content = "edited";
        File.WriteAllText(filePath, "changed externally");

        sut.RevertToDiskCommand.Execute(null);

        Assert.Equal("changed externally", sut.Content);
        Assert.False(sut.IsDirty);
    }

    [Fact]
    public void ExplainCommand_NotLoading_CanExecute()
    {
        var sut = CreateSut("a.txt", "content");

        Assert.True(sut.ExplainCommand.CanExecute(null));
    }

    [Fact]
    public void ExplainCommand_AlreadyLoading_CannotExecute()
    {
        var sut = CreateSut("a.txt", "content");

        sut.IsLoadingOverview = true;

        Assert.False(sut.ExplainCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExplainAsync_Succeeds_PopulatesFileOverviewAndSendsFilePathAndContent()
    {
        var sut = CreateSut("a.txt", "file content");
        _llmRunner.ResultToReturn = new() { Success = true, Output = "  This file does X.  " };

        await sut.ExplainCommand.ExecuteAsync(null);

        Assert.Equal("This file does X.", sut.FileOverview);
        Assert.False(sut.IsLoadingOverview);
        Assert.Contains(sut.FilePath, _llmRunner.LastPrompt);
        Assert.Contains("file content", _llmRunner.LastPrompt);
        Assert.Null(_llmRunner.LastResumeSessionId);
    }

    [Fact]
    public async Task ExplainAsync_Fails_PopulatesFileOverviewWithFormattedError()
    {
        var sut = CreateSut("a.txt", "content");
        _llmRunner.ResultToReturn = new() { Success = false, ErrorMessage = "boom" };

        await sut.ExplainCommand.ExecuteAsync(null);

        Assert.Equal("Failed to explain this file: boom", sut.FileOverview);
        Assert.False(sut.IsLoadingOverview);
    }
}
