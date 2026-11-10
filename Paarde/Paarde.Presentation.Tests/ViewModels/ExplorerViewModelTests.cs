using Paarde.Presentation.Models;
using Paarde.Presentation.Tests.Fakes;
using Paarde.Core.Models;
using Paarde.Core.Services;
using Paarde.Core.ViewModels;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;

namespace Paarde.Presentation.Tests.ViewModels;

public class ExplorerViewModelTests : IDisposable
{
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakePromptResultStore _promptResultStore = new();
    private readonly FakeLlmRunner _llmRunner = new();
    private readonly FakeFileChangeApplyService _fileChangeApplyService = new();
    private readonly FakeDialogService _dialogService = new();
    private readonly FakeLanguageServerSessionManager _languageServerSessionManager = new();
    private readonly FakeFolderPickerService _folderPickerService = new();
    private readonly FakeNavigationService _navigationService = new();
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

    // ExplorerViewModel now takes a real AssistantPanelViewModel (which itself wraps
    // a real PromptViewModel/TutorialViewModel, each only needing fakes for their own
    // dependencies) rather than a fake, since AttachExplorer/FileFocusRequested below need the
    // genuine wiring, not just a constructor to satisfy.
    private AssistantPanelViewModel CreateAssistantPanel()
    {
        var compose = new PromptViewModel(_llmRunner, _localSettingsService, _folderPickerService, new FakeFilePickerService(), _promptResultStore, new PromptTemplateProvider(), new FakeRunSessionStore(), new FakeCodebaseIndexService(), new FakeWorkingDirectoryContext(), new FakeUiDispatcher());
        var review = new TutorialViewModel(_promptResultStore, new FakeApplyOrchestrationService(), new FakeApplyHistoryLog(), _dialogService, _llmRunner, new PromptTemplateProvider(), new FakeFileSavePickerService(), _localSettingsService, new FakeBuildTestRunner());

        return new AssistantPanelViewModel(compose, review, _promptResultStore, _localSettingsService, _navigationService);
    }

    private ExplorerViewModel CreateSut() => new(_localSettingsService, _promptResultStore, _llmRunner, _fileChangeApplyService, _dialogService, _languageServerSessionManager, _folderPickerService, CreateAssistantPanel(), new FakeUiDispatcher());

    private async Task SetSavedWorkingDirectoryAsync(string path) =>
        await _localSettingsService.SaveSettingAsync(WorkingDirectorySettingsKeys.WorkingDirectory, path);

    [Fact]
    public void OnNavigatedTo_NoSavedWorkingDirectory_HasNoRootAndExplainsWhy()
    {
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.Null(sut.Root);
        Assert.NotEmpty(sut.StatusMessage);
        Assert.Equal(NotificationSeverity.Warning, sut.StatusSeverity);
    }

    [Fact]
    public async Task OnNavigatedTo_SavedWorkingDirectoryThatNoLongerExists_HasNoRootAndExplainsWhy()
    {
        await SetSavedWorkingDirectoryAsync(Path.Combine(_workingDirectory, "does-not-exist"));
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.Null(sut.Root);
        Assert.NotEmpty(sut.StatusMessage);
        Assert.Equal(NotificationSeverity.Error, sut.StatusSeverity);
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
        Assert.Equal(NotificationSeverity.Informational, sut.StatusSeverity);
    }

    // Quick Open navigates here with the absolute path of the file to open.
    [Fact]
    public async Task OnNavigatedTo_StringParameterPointingAtAnExistingFile_OpensItAsATab()
    {
        var filePath = Path.Combine(_workingDirectory, "a.txt");
        File.WriteAllText(filePath, "file content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();

        sut.OnNavigatedTo(filePath);

        var tab = Assert.Single(sut.OpenTabs);
        Assert.Same(tab, sut.SelectedTab);
        Assert.Equal(filePath, tab.FilePath);
        Assert.Equal("file content", tab.Content);
    }

    // Regression guard: every other OnNavigatedTo test in this class passes null (or a type that
    // isn't a string), and must keep behaving exactly as before this epic's change - no tab opened,
    // no exception.
    [Fact]
    public async Task OnNavigatedTo_StringParameterPointingAtAFileThatDoesNotExist_DoesNotOpenATab()
    {
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();

        sut.OnNavigatedTo(Path.Combine(_workingDirectory, "does-not-exist.txt"));

        Assert.Empty(sut.OpenTabs);
        Assert.Null(sut.SelectedTab);
    }

    [Fact]
    public async Task OnNavigatedTo_NonStringParameter_DoesNotOpenATab()
    {
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();

        sut.OnNavigatedTo(new object());

        Assert.Empty(sut.OpenTabs);
        Assert.Null(sut.SelectedTab);
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

    // Every open tab keeps its own content now, so switching the tree selection
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

    // A prompt run adding to IPromptResultStore.Files opens/activates a tab for
    // each new file, carrying its proposed change along as PendingChange.
    [Fact]
    public async Task PromptResultStoreFilesChanged_NewFileAdded_OpensTabWithPendingChange()
    {
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        var change = new ProposedFileChangeViewModel("a.txt", "proposed", "original");

        _promptResultStore.SetResult("tutorial", [change], _workingDirectory);

        var tab = Assert.Single(sut.OpenTabs);
        Assert.Same(tab, sut.SelectedTab);
        Assert.Same(change, tab.PendingChange);
    }

    // An already-open tab keeps whichever PendingChange it already had rather than
    // letting a later, unrelated prompt run's change for the same file clobber it.
    [Fact]
    public async Task PromptResultStoreFilesChanged_TabAlreadyHasAPendingChange_DoesNotClobberIt()
    {
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        var firstChange = new ProposedFileChangeViewModel("a.txt", "first proposed", "original");
        _promptResultStore.SetResult("tutorial", [firstChange], _workingDirectory);
        var tab = Assert.Single(sut.OpenTabs);
        var secondChange = new ProposedFileChangeViewModel("a.txt", "second proposed", "original");

        _promptResultStore.SetResult("tutorial", [secondChange], _workingDirectory);

        Assert.Same(tab, Assert.Single(sut.OpenTabs));
        Assert.Same(firstChange, tab.PendingChange);
    }

    // Lets the user pick a working directory from the Explorer page itself.
    [Fact]
    public async Task OpenFolderCommand_FolderPicked_SavesSettingUpdatesPathAndRefreshesTree()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        _folderPickerService.FolderToReturn = _workingDirectory;
        var sut = CreateSut();

        await sut.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal(_workingDirectory, sut.WorkingDirectoryPath);
        Assert.Equal("a.txt", Assert.Single(sut.Root!.Children).Name);
        Assert.Equal(_workingDirectory, await _localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory));
    }

    [Fact]
    public async Task OpenFolderCommand_NoFolderPicked_LeavesWorkingDirectoryUnchanged()
    {
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        _folderPickerService.FolderToReturn = null;

        await sut.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal(_workingDirectory, sut.WorkingDirectoryPath);
    }

    // Review's file list / a step card's "View in Diff" asks to focus a proposed
    // change - the tab is asked to scroll to the targeted hunk once it's open.
    [Fact]
    public async Task ReviewFileFocusRequested_WithHunk_OpensTabAndForwardsScrollRequest()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single(node => node.Name == "a.txt");
        var tab = Assert.Single(sut.OpenTabs);
        var change = new ProposedFileChangeViewModel("a.txt", "proposed", "original");
        var hunk = new DiffHunk(1, 1, 1, 1, Array.Empty<DiffLine>(), 0, 1);
        DiffHunk? received = null;
        tab.ScrollToHunkRequested += h => received = h;

        sut.AssistantPanel.Review.RequestFileFocus(change, hunk);

        Assert.Same(tab, sut.SelectedTab);
        Assert.Same(hunk, received);
    }

    // Every newly opened tab starts out in the primary group, same
    // as it always has (PrimaryGroupTabs mirrors OpenTabs until something is split right).
    [Fact]
    public async Task OpenOrActivateTab_NewTab_AddsToPrimaryGroup()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);

        sut.SelectedNode = sut.Root!.Children.Single();

        var tab = Assert.Single(sut.OpenTabs);
        Assert.Same(tab, Assert.Single(sut.PrimaryGroupTabs));
        Assert.Empty(sut.SecondaryGroupTabs);
        Assert.False(sut.HasSecondaryGroupTabs);
    }

    [Fact]
    public async Task RequestSplitRight_MovesTabFromPrimaryToSecondaryGroup()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();
        var tab = Assert.Single(sut.OpenTabs);

        tab.RequestSplitRightCommand.Execute(null);

        Assert.Equal(1, tab.TabGroup);
        Assert.Empty(sut.PrimaryGroupTabs);
        Assert.Same(tab, Assert.Single(sut.SecondaryGroupTabs));
        Assert.True(sut.HasSecondaryGroupTabs);
    }

    [Fact]
    public async Task CloseSelectedTabCommand_ClosingTheOnlySecondaryGroupTab_CollapsesHasSecondaryGroupTabs()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single();
        var tab = sut.SelectedTab!;
        tab.RequestSplitRightCommand.Execute(null);

        await sut.CloseSelectedTabCommand.ExecuteAsync(null);

        Assert.Empty(sut.SecondaryGroupTabs);
        Assert.False(sut.HasSecondaryGroupTabs);
    }

    // Pinning moves a tab to right after the last already-pinned
    // tab in its own group - unpinning leaves its position untouched.
    [Fact]
    public async Task TogglePin_PinningATab_MovesItBeforeAllUnpinnedTabsInItsGroup()
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
        var tabC = sut.OpenTabs[2];

        tabC.TogglePinCommand.Execute(null);

        Assert.Equal(["c.txt", "a.txt", "b.txt"], sut.PrimaryGroupTabs.Select(t => t.FileName));
    }

    [Fact]
    public async Task TogglePin_UnpinningATab_LeavesItsPositionUnchanged()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content a");
        File.WriteAllText(Path.Combine(_workingDirectory, "b.txt"), "content b");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single(node => node.Name == "a.txt");
        sut.SelectedNode = sut.Root!.Children.Single(node => node.Name == "b.txt");
        var tabB = sut.OpenTabs[1];
        tabB.TogglePinCommand.Execute(null);

        tabB.TogglePinCommand.Execute(null);

        // Pinning tabB already moved it to the front; unpinning doesn't move it back.
        Assert.Equal(["b.txt", "a.txt"], sut.PrimaryGroupTabs.Select(t => t.FileName));
        Assert.False(tabB.IsPinned);
    }

    // Raises the view's event rather than touching layout itself -
    // ExplorerView.axaml.cs owns the live ColumnDefinitions.
    [Fact]
    public void ToggleZenModeCommand_RaisesZenModeToggled()
    {
        var sut = CreateSut();
        var raisedCount = 0;
        sut.ZenModeToggled += () => raisedCount++;

        sut.ToggleZenModeCommand.Execute(null);

        Assert.Equal(1, raisedCount);
    }

    // OpenMruSwitcher snapshots the most-recently-activated order
    // and highlights the second entry (the previously-active tab, not the still-current one) -
    // matching the "first Ctrl+Tap press jumps to the previous tab" convention.
    [Fact]
    public async Task OpenMruSwitcher_HighlightsThePreviouslyActiveTab()
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

        sut.OpenMruSwitcher();

        Assert.True(sut.IsMruSwitcherOpen);
        Assert.Equal(["c.txt", "b.txt", "a.txt"], sut.MruSwitcherItems.Select(t => t.FileName));
        Assert.Equal("b.txt", sut.MruSwitcherHighlighted!.FileName);
    }

    [Fact]
    public async Task AdvanceMruSwitcher_CyclesThroughEveryEntryAndWrapsAround()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content a");
        File.WriteAllText(Path.Combine(_workingDirectory, "b.txt"), "content b");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single(node => node.Name == "a.txt");
        sut.SelectedNode = sut.Root!.Children.Single(node => node.Name == "b.txt");
        sut.OpenMruSwitcher();

        sut.AdvanceMruSwitcher();

        Assert.Equal("b.txt", sut.MruSwitcherHighlighted!.FileName);
    }

    [Fact]
    public async Task CommitMruSwitcher_ActivatesTheHighlightedTabAndClosesThePopup()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content a");
        File.WriteAllText(Path.Combine(_workingDirectory, "b.txt"), "content b");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single(node => node.Name == "a.txt");
        sut.SelectedNode = sut.Root!.Children.Single(node => node.Name == "b.txt");
        var tabA = sut.OpenTabs[0];
        sut.OpenMruSwitcher();

        sut.CommitMruSwitcher();

        Assert.Same(tabA, sut.SelectedTab);
        Assert.False(sut.IsMruSwitcherOpen);
    }

    [Fact]
    public async Task ClosingTheMostRecentlyUsedTab_RemovesItFromTheMruOrder()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "content a");
        File.WriteAllText(Path.Combine(_workingDirectory, "b.txt"), "content b");
        await SetSavedWorkingDirectoryAsync(_workingDirectory);
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.SelectedNode = sut.Root!.Children.Single(node => node.Name == "a.txt");
        sut.SelectedNode = sut.Root!.Children.Single(node => node.Name == "b.txt");

        await sut.CloseSelectedTabCommand.ExecuteAsync(null);
        sut.OpenMruSwitcher();

        Assert.Equal(["a.txt"], sut.MruSwitcherItems.Select(t => t.FileName));
    }
}
