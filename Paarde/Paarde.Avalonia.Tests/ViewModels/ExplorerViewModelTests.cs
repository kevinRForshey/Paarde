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
    public async Task SelectedNode_SetToAFile_OpensItAsATab()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "file content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        var fileNode = Assert.Single(sut.Root!.Children);

        sut.SelectedNode = fileNode;

        var tab = Assert.Single(sut.OpenTabs);
        Assert.Same(tab, sut.SelectedTab);
        Assert.Equal(fileNode.FullPath, tab.FilePath);
        Assert.Equal("file content", tab.Content);
    }

    [Fact]
    public async Task SelectedNode_SetToADirectory_DoesNotOpenATab()
    {
        Directory.CreateDirectory(Path.Combine(_workingDirectory, "src"));
        File.WriteAllText(Path.Combine(_workingDirectory, "src", "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        var folderNode = Assert.Single(sut.Root!.Children);

        sut.SelectedNode = folderNode;

        Assert.Empty(sut.OpenTabs);
        Assert.Null(sut.SelectedTab);
    }

    [Fact]
    public async Task SelectedNode_SetToNull_DoesNotCloseAnyOpenTabs()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();

        sut.SelectedNode = null;

        Assert.Single(sut.OpenTabs);
    }

    [Fact]
    public async Task SelectedNode_SelectingAnAlreadyOpenFile_ActivatesTheExistingTabInsteadOfOpeningADuplicate()
    {
        Directory.CreateDirectory(Path.Combine(_workingDirectory, "src"));
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content a");
        File.WriteAllText(Path.Combine(_workingDirectory, "src", "b.txt"), "content b");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        var fileNodeA = sut.Root!.Children.Single(node => node.Name == "a.txt");
        var srcFolder = sut.Root!.Children.Single(node => node.Name == "src");
        var fileNodeB = srcFolder.Children.Single();
        sut.SelectedNode = fileNodeA;
        sut.SelectedTab!.Content = "edited a";
        sut.SelectedNode = fileNodeB;

        sut.SelectedNode = fileNodeA;

        Assert.Equal(2, sut.OpenTabs.Count);
        Assert.Equal("edited a", sut.SelectedTab!.Content);
    }

    [Fact]
    public async Task RefreshTreeCommand_AFileWasSelected_ClearsTheStaleTreeSelectionButKeepsTheTabOpen()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();

        sut.RefreshTreeCommand.Execute(null);

        Assert.Null(sut.SelectedNode);
        Assert.Single(sut.OpenTabs);
    }

    // Epic 23, T23.1.1: every open tab keeps its own content now, so switching the tree selection
    // can no longer discard anything - the T10.2.5 discard-confirmation this page used to show on
    // SelectedNode changing while dirty (SelectedNode_ChangedWhileDirty_PromptsAndDiscardsWhenConfirmed/
    // RevertsSelectionWhenDeclined) no longer applies. This replaces those two tests: selecting a
    // second file while the first tab is dirty just opens a second tab, without prompting or
    // touching the first tab's edits.
    [Fact]
    public async Task SelectedNode_ChangedWhileAnotherTabIsDirty_OpensTheNewTabWithoutPromptingOrDiscardingTheOther()
    {
        Directory.CreateDirectory(Path.Combine(_workingDirectory, "src"));
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original a");
        File.WriteAllText(Path.Combine(_workingDirectory, "src", "b.txt"), "original b");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        var fileNodeA = sut.Root!.Children.Single(node => node.Name == "a.txt");
        sut.SelectedNode = fileNodeA;
        var tabA = sut.SelectedTab!;
        tabA.Content = "edited a";

        var srcFolder = sut.Root!.Children.Single(node => node.Name == "src");
        sut.SelectedNode = srcFolder.Children.Single();

        Assert.Equal(0, _dialogService.ConfirmCallCount);
        Assert.Equal(2, sut.OpenTabs.Count);
        Assert.Equal("edited a", tabA.Content);
        Assert.True(tabA.IsDirty);
        Assert.Equal("original b", sut.SelectedTab!.Content);
    }

    [Fact]
    public async Task OpenTab_ContentEdited_MarksHasUnsavedChanges()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();

        sut.SelectedTab!.Content = "edited";

        Assert.True(sut.HasUnsavedChanges);
    }

    [Fact]
    public void CloseSelectedTabCommand_NoTabOpen_CannotExecute()
    {
        var sut = CreateSut();

        Assert.False(sut.CloseSelectedTabCommand.CanExecute(null));
    }

    [Fact]
    public async Task CloseSelectedTabCommand_NotDirty_ClosesWithoutPrompting()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();

        await sut.CloseSelectedTabCommand.ExecuteAsync(null);

        Assert.Equal(0, _dialogService.ConfirmCallCount);
        Assert.Empty(sut.OpenTabs);
        Assert.Null(sut.SelectedTab);
    }

    [Fact]
    public async Task CloseSelectedTabCommand_Dirty_PromptsAndClosesWhenConfirmed()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();
        sut.SelectedTab!.Content = "edited";
        _dialogService.ConfirmResultToReturn = true;

        await sut.CloseSelectedTabCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogService.ConfirmCallCount);
        Assert.Empty(sut.OpenTabs);
    }

    [Fact]
    public async Task CloseSelectedTabCommand_Dirty_KeepsTheTabOpenWhenDeclined()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();
        var tab = sut.SelectedTab!;
        tab.Content = "edited";
        _dialogService.ConfirmResultToReturn = false;

        await sut.CloseSelectedTabCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogService.ConfirmCallCount);
        Assert.Same(tab, Assert.Single(sut.OpenTabs));
        Assert.Same(tab, sut.SelectedTab);
    }

    [Fact]
    public async Task CloseSelectedTabCommand_ClosingTheMiddleTab_SelectsItsNeighbor()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content a");
        File.WriteAllText(Path.Combine(_workingDirectory, "b.txt"), "content b");
        File.WriteAllText(Path.Combine(_workingDirectory, "c.txt"), "content c");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single(node => node.Name == "a.txt");
        sut.SelectedNode = sut.Root!.Children.Single(node => node.Name == "b.txt");
        sut.SelectedNode = sut.Root!.Children.Single(node => node.Name == "c.txt");
        var tabC = sut.SelectedTab;
        var tabB = sut.OpenTabs[1];
        sut.SelectedTab = tabB;

        await sut.CloseSelectedTabCommand.ExecuteAsync(null);

        Assert.Equal(2, sut.OpenTabs.Count);
        Assert.Same(tabC, sut.SelectedTab);
    }
}
