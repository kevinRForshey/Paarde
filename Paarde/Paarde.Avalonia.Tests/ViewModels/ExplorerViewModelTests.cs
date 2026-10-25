using Paarde.Avalonia.Services;
using Paarde.Avalonia.Tests.Fakes;
using Paarde.Avalonia.ViewModels;
using Paarde.Core.ViewModels;

namespace Paarde.Avalonia.Tests.ViewModels;

public class ExplorerViewModelTests : IDisposable
{
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakePromptResultStore _promptResultStore = new();
    private readonly FakeLlmRunner _llmRunner = new();
    private readonly FakeFileChangeApplyService _fileChangeApplyService = new();
    private readonly FakeDialogService _dialogService = new();
    private readonly FakeLanguageServerSessionManager _languageServerSessionManager = new();
    private readonly string _workingDirectory;

    public ExplorerViewModelTests()
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

    private ExplorerViewModel CreateSut() => new(_localSettingsService, _promptResultStore, _llmRunner, _fileChangeApplyService, _dialogService, _languageServerSessionManager);

    private async Task SetSavedWorkingDirectoryAsync(string path) =>
        await _localSettingsService.SaveSettingAsync(WorkingDirectorySettingsKeys.WorkingDirectory, path);

    [Fact]
    public void OnNavigatedTo_NoSavedWorkingDirectory_HasNoRootAndExplainsWhy()
    {
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.Null(sut.Root);
        Assert.NotEmpty(sut.StatusMessage);
    }

    [Fact]
    public async Task OnNavigatedTo_SavedWorkingDirectoryThatNoLongerExists_HasNoRootAndExplainsWhy()
    {
        await SetSavedWorkingDirectoryAsync(Path.Combine(_workingDirectory, "does-not-exist"));
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.Null(sut.Root);
        Assert.NotEmpty(sut.StatusMessage);
    }

    [Fact]
    public async Task OnNavigatedTo_SavedWorkingDirectory_BuildsTheFileTreeAndClearsAnyStatusMessage()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.NotNull(sut.Root);
        Assert.Equal("a.txt", Assert.Single(sut.Root!.Children).Name);
        Assert.Empty(sut.StatusMessage);
    }

    [Fact]
    public async Task RefreshTreeCommand_AfterAFileIsAdded_PicksUpTheNewFile()
    {
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        Assert.Empty(sut.Root!.Children);

        File.WriteAllText(Path.Combine(_workingDirectory, "new.txt"), "content");
        sut.RefreshTreeCommand.Execute(null);

        Assert.Equal("new.txt", Assert.Single(sut.Root!.Children).Name);
    }

    [Fact]
    public async Task OnNavigatedTo_FileIsPartOfTheCurrentProposal_IsMarkedTouched()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        File.WriteAllText(Path.Combine(_workingDirectory, "b.txt"), "content");
        _promptResultStore.SetResult("tutorial", [new ProposedFileChangeViewModel("a.txt", "proposed", "original")], _workingDirectory);
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        var touched = sut.Root!.Children.Single(node => node.Name == "a.txt");
        var untouched = sut.Root!.Children.Single(node => node.Name == "b.txt");
        Assert.True(touched.IsTouched);
        Assert.False(untouched.IsTouched);
    }

    [Fact]
    public async Task OnNavigatedTo_NoCurrentProposal_NothingIsMarkedTouched()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.False(Assert.Single(sut.Root!.Children).IsTouched);
    }

    [Fact]
    public async Task OnNavigatedTo_TouchedFileIsInASubdirectory_IsMarkedTouched()
    {
        Directory.CreateDirectory(Path.Combine(_workingDirectory, "src"));
        File.WriteAllText(Path.Combine(_workingDirectory, "src", "a.txt"), "content");
        _promptResultStore.SetResult("tutorial", [new ProposedFileChangeViewModel(Path.Combine("src", "a.txt"), "proposed", "original")], _workingDirectory);
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        var srcFolder = Assert.Single(sut.Root!.Children);
        Assert.True(Assert.Single(srcFolder.Children).IsTouched);
    }

    [Fact]
    public async Task SelectedNode_SetToAFile_LoadsItsContentForPreview()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "file content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        var fileNode = Assert.Single(sut.Root!.Children);

        sut.SelectedNode = fileNode;

        Assert.Equal(fileNode.FullPath, sut.SelectedFilePath);
        Assert.Equal("file content", sut.SelectedFileContent);
    }

    [Fact]
    public async Task SelectedNode_SetToADirectory_ClearsThePreview()
    {
        Directory.CreateDirectory(Path.Combine(_workingDirectory, "src"));
        File.WriteAllText(Path.Combine(_workingDirectory, "src", "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        var folderNode = Assert.Single(sut.Root!.Children);

        sut.SelectedNode = folderNode;

        Assert.Empty(sut.SelectedFilePath);
        Assert.Empty(sut.SelectedFileContent);
    }

    [Fact]
    public async Task SelectedNode_SetToNull_ClearsThePreview()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();

        sut.SelectedNode = null;

        Assert.Empty(sut.SelectedFilePath);
        Assert.Empty(sut.SelectedFileContent);
    }

    [Fact]
    public async Task RefreshTreeCommand_AFileWasSelected_ClearsTheStaleSelectionAndPreview()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();

        sut.RefreshTreeCommand.Execute(null);

        Assert.Null(sut.SelectedNode);
        Assert.Empty(sut.SelectedFilePath);
        Assert.Empty(sut.SelectedFileContent);
    }

    [Fact]
    public void ExplainSelectedFileCommand_NoFileSelected_CannotExecute()
    {
        var sut = CreateSut();

        Assert.False(sut.ExplainSelectedFileCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExplainSelectedFileCommand_FileSelected_CanExecute()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();

        Assert.True(sut.ExplainSelectedFileCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExplainSelectedFileCommand_AlreadyLoading_CannotExecute()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();

        sut.IsLoadingOverview = true;

        Assert.False(sut.ExplainSelectedFileCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExplainSelectedFileAsync_Succeeds_PopulatesFileOverviewAndSendsFilePathAndContent()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "file content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();
        _llmRunner.ResultToReturn = new() { Success = true, Output = "  This file does X.  " };

        await sut.ExplainSelectedFileCommand.ExecuteAsync(null);

        Assert.Equal("This file does X.", sut.FileOverview);
        Assert.False(sut.IsLoadingOverview);
        Assert.Contains(sut.SelectedFilePath, _llmRunner.LastPrompt);
        Assert.Contains("file content", _llmRunner.LastPrompt);
        Assert.Null(_llmRunner.LastResumeSessionId);
    }

    [Fact]
    public async Task ExplainSelectedFileAsync_Fails_PopulatesFileOverviewWithFormattedError()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();
        _llmRunner.ResultToReturn = new() { Success = false, ErrorMessage = "boom" };

        await sut.ExplainSelectedFileCommand.ExecuteAsync(null);

        Assert.Equal("Failed to explain this file: boom", sut.FileOverview);
        Assert.False(sut.IsLoadingOverview);
    }

    [Fact]
    public async Task SelectedNode_ChangedAfterExplaining_ClearsThePreviousOverview()
    {
        Directory.CreateDirectory(Path.Combine(_workingDirectory, "src"));
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        File.WriteAllText(Path.Combine(_workingDirectory, "src", "b.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single(node => node.Name == "a.txt");
        _llmRunner.ResultToReturn = new() { Success = true, Output = "Explanation of a.txt" };
        await sut.ExplainSelectedFileCommand.ExecuteAsync(null);
        Assert.NotEmpty(sut.FileOverview);

        var srcFolder = sut.Root!.Children.Single(node => node.Name == "src");
        sut.SelectedNode = srcFolder.Children.Single();

        Assert.Empty(sut.FileOverview);
    }

    [Fact]
    public async Task SelectedFileContent_Edited_MarksTheFileDirty()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();

        sut.SelectedFileContent = "edited";

        Assert.True(sut.IsFileDirty);
        Assert.True(sut.HasUnsavedChanges);
    }

    [Fact]
    public async Task SelectedFileContent_EditedBackToOriginal_ClearsDirty()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();
        sut.SelectedFileContent = "edited";

        sut.SelectedFileContent = "original";

        Assert.False(sut.IsFileDirty);
    }

    [Fact]
    public void SaveSelectedFileCommand_NotDirty_CannotExecute()
    {
        var sut = CreateSut();

        Assert.False(sut.SaveSelectedFileCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaveSelectedFileCommand_Dirty_Succeeds_WritesAndClearsDirty()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();
        sut.SelectedFileContent = "edited";

        Assert.True(sut.SaveSelectedFileCommand.CanExecute(null));
        sut.SaveSelectedFileCommand.Execute(null);

        Assert.False(sut.IsFileDirty);
        Assert.Equal(1, _fileChangeApplyService.WriteRawCallCount);
        Assert.Equal("a.txt", _fileChangeApplyService.LastWriteRawFilePath);
        Assert.Equal("edited", _fileChangeApplyService.LastWriteRawContent);
        Assert.NotEmpty(sut.StatusMessage);
    }

    [Fact]
    public async Task SaveSelectedFileCommand_ProtectedPath_IsBlockedAndDoesNotWrite()
    {
        Directory.CreateDirectory(Path.Combine(_workingDirectory, "bin"));
        File.WriteAllText(Path.Combine(_workingDirectory, "bin", "a.txt"), "original");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        var binFolder = sut.Root!.Children.Single(node => node.Name == "bin");
        sut.SelectedNode = binFolder.Children.Single();
        sut.SelectedFileContent = "edited";

        sut.SaveSelectedFileCommand.Execute(null);

        Assert.Equal(0, _fileChangeApplyService.WriteRawCallCount);
        Assert.True(sut.IsFileDirty);
        Assert.NotEmpty(sut.StatusMessage);
    }

    [Fact]
    public async Task SaveSelectedFileCommand_FileChangedOnDiskSinceOpened_IsBlockedAndDoesNotWrite()
    {
        var filePath = Path.Combine(_workingDirectory, "a.txt");
        File.WriteAllText(filePath, "original");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();
        sut.SelectedFileContent = "edited";
        File.WriteAllText(filePath, "changed externally");

        sut.SaveSelectedFileCommand.Execute(null);

        Assert.Equal(0, _fileChangeApplyService.WriteRawCallCount);
        Assert.True(sut.IsFileDirty);
        Assert.NotEmpty(sut.StatusMessage);
    }

    [Fact]
    public void RevertSelectedFileToDiskCommand_NotDirty_CannotExecute()
    {
        var sut = CreateSut();

        Assert.False(sut.RevertSelectedFileToDiskCommand.CanExecute(null));
    }

    [Fact]
    public async Task RevertSelectedFileToDiskCommand_Dirty_ReloadsFromDiskAndClearsDirty()
    {
        var filePath = Path.Combine(_workingDirectory, "a.txt");
        File.WriteAllText(filePath, "original");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();
        sut.SelectedFileContent = "edited";
        File.WriteAllText(filePath, "changed externally");

        sut.RevertSelectedFileToDiskCommand.Execute(null);

        Assert.Equal("changed externally", sut.SelectedFileContent);
        Assert.False(sut.IsFileDirty);
    }

    [Fact]
    public async Task SelectedNode_ReselectingTheSameFile_DoesNotDiscardEditsOrReload()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        var fileNode = sut.Root!.Children.Single();
        sut.SelectedNode = fileNode;
        sut.SelectedFileContent = "edited";

        sut.SelectedNode = fileNode;

        Assert.Equal("edited", sut.SelectedFileContent);
        Assert.True(sut.IsFileDirty);
        Assert.Equal(0, _dialogService.ConfirmCallCount);
    }

    [Fact]
    public async Task SelectedNode_ChangedWhileDirty_PromptsAndDiscardsWhenConfirmed()
    {
        Directory.CreateDirectory(Path.Combine(_workingDirectory, "src"));
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original a");
        File.WriteAllText(Path.Combine(_workingDirectory, "src", "b.txt"), "original b");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single(node => node.Name == "a.txt");
        sut.SelectedFileContent = "edited a";
        _dialogService.ConfirmResultToReturn = true;

        var srcFolder = sut.Root!.Children.Single(node => node.Name == "src");
        sut.SelectedNode = srcFolder.Children.Single();

        Assert.Equal(1, _dialogService.ConfirmCallCount);
        Assert.Equal("original b", sut.SelectedFileContent);
        Assert.False(sut.IsFileDirty);
    }

    [Fact]
    public async Task SelectedNode_ChangedWhileDirty_RevertsSelectionWhenDeclined()
    {
        Directory.CreateDirectory(Path.Combine(_workingDirectory, "src"));
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original a");
        File.WriteAllText(Path.Combine(_workingDirectory, "src", "b.txt"), "original b");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        var fileNodeA = sut.Root!.Children.Single(node => node.Name == "a.txt");
        sut.SelectedNode = fileNodeA;
        sut.SelectedFileContent = "edited a";
        _dialogService.ConfirmResultToReturn = false;

        var srcFolder = sut.Root!.Children.Single(node => node.Name == "src");
        sut.SelectedNode = srcFolder.Children.Single();

        Assert.Equal(1, _dialogService.ConfirmCallCount);
        Assert.Equal(fileNodeA, sut.SelectedNode);
        Assert.Equal("edited a", sut.SelectedFileContent);
        Assert.True(sut.IsFileDirty);
    }
}
