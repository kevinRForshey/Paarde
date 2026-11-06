using FluentAvalonia.UI.Controls;
using Paarde.Avalonia.Tests.Fakes;
using Paarde.Avalonia.ViewModels;
using Paarde.Core.Models;
using Paarde.Core.ViewModels;

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
        Assert.Equal(InfoBarSeverity.Success, sut.StatusSeverity);
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
        Assert.Equal(InfoBarSeverity.Error, sut.StatusSeverity);
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
        Assert.Equal(InfoBarSeverity.Warning, sut.StatusSeverity);
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

    [Fact]
    public void EffectiveContent_NoPendingChange_RoutesThroughContent()
    {
        var sut = CreateSut("a.txt", "original");

        sut.EffectiveContent = "edited";

        Assert.Equal("edited", sut.Content);
        Assert.True(sut.IsDirty);
    }

    [Fact]
    public void EffectiveContent_WithPendingChange_RoutesThroughEditedContentAndLeavesContentAlone()
    {
        var sut = CreateSut("a.txt", "original");
        sut.PendingChange = new ProposedFileChangeViewModel("a.txt", "proposed", "original");

        sut.EffectiveContent = "edited by user";

        Assert.Equal("edited by user", sut.PendingChange.EditedContent);
        Assert.Equal("original", sut.Content);
        Assert.False(sut.IsDirty);
    }

    [Fact]
    public void EffectiveContent_PendingChangeEditedContentChanges_RaisesPropertyChanged()
    {
        var sut = CreateSut("a.txt", "original");
        var pendingChange = new ProposedFileChangeViewModel("a.txt", "proposed", "original");
        sut.PendingChange = pendingChange;
        var raisedProperties = new List<string?>();
        sut.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        pendingChange.EditedContent = "refined elsewhere";

        Assert.Contains(nameof(sut.EffectiveContent), raisedProperties);
        Assert.Equal("refined elsewhere", sut.EffectiveContent);
    }

    [Fact]
    public void RequestScrollTo_RaisesScrollToHunkRequestedWithTheSameHunk()
    {
        var sut = CreateSut("a.txt", "original");
        var hunk = new DiffHunk(1, 1, 1, 1, Array.Empty<DiffLine>(), 0, 1);
        DiffHunk? received = null;
        sut.ScrollToHunkRequested += h => received = h;

        sut.RequestScrollTo(hunk);

        Assert.Same(hunk, received);
    }

    [Fact]
    public void TogglePinCommand_RaisesPinToggledAndFlipsIsPinned()
    {
        var sut = CreateSut("a.txt", "original");
        var raisedCount = 0;
        sut.PinToggled += () => raisedCount++;

        sut.TogglePinCommand.Execute(null);

        Assert.True(sut.IsPinned);
        Assert.Equal(1, raisedCount);

        sut.TogglePinCommand.Execute(null);

        Assert.False(sut.IsPinned);
        Assert.Equal(2, raisedCount);
    }

    [Fact]
    public void PinMenuItemLabel_ReflectsIsPinned()
    {
        var sut = CreateSut("a.txt", "original");

        Assert.Equal("Pin", sut.PinMenuItemLabel);

        sut.IsPinned = true;

        Assert.Equal("Unpin", sut.PinMenuItemLabel);
    }

    [Fact]
    public void RequestSplitRightCommand_RaisesSplitRightRequested()
    {
        var sut = CreateSut("a.txt", "original");
        var raisedCount = 0;
        sut.SplitRightRequested += () => raisedCount++;

        sut.RequestSplitRightCommand.Execute(null);

        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public void CanSplitRight_FalseOnceInTheSecondaryGroup()
    {
        var sut = CreateSut("a.txt", "original");

        Assert.True(sut.CanSplitRight);

        sut.TabGroup = 1;

        Assert.False(sut.CanSplitRight);
        Assert.False(sut.RequestSplitRightCommand.CanExecute(null));
    }
}
