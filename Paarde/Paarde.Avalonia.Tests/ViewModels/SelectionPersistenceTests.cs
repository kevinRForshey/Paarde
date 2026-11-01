using Paarde.Avalonia.Tests.Fakes;
using Paarde.Avalonia.ViewModels;
using Paarde.Core.Services;
using Paarde.Core.ViewModels;

namespace Paarde.Avalonia.Tests.ViewModels;

// Covers F3.2/T3.2.4: selection state (ProposedHunk.IsSelected / ProposedFileChangeViewModel.IsSelected)
// must survive navigating between the Tutorial and Diffs pages. Both ViewModels pull the same
// ProposedFileChangeViewModel instances straight out of IPromptResultStore.Files rather than copying
// them, so this is really a regression test pinning that sharing - not new selection logic.
public class SelectionPersistenceTests
{
    [Fact]
    public void HunkSelection_ToggledOnTutorialPage_IsVisibleOnDiffsPage()
    {
        var promptResultStore = new FakePromptResultStore();
        var hunk = new ProposedHunk("Step 1", string.Empty, "x", null);
        promptResultStore.SetResult(
            "tutorial",
            [new ProposedFileChangeViewModel("a.txt", "proposed", "original") { Hunks = new[] { hunk } }],
            "/tmp");

        var tutorialViewModel = new TutorialViewModel(promptResultStore, new FakeApplyOrchestrationService(), new FakeApplyHistoryLog(), new FakeDialogService(), new FakeLlmRunner(), new PromptTemplateProvider(), new FakeFileSavePickerService(), new FakeLocalSettingsService(), new FakeBuildTestRunner());
        var diffsViewModel = new DiffsViewModel(promptResultStore, new FakeNavigationService());
        diffsViewModel.OnNavigatedTo(null!);

        tutorialViewModel.Files[0].Hunks[0].IsSelected = false;

        Assert.False(diffsViewModel.Files[0].Hunks[0].IsSelected);
        Assert.False(diffsViewModel.Files[0].IsSelected);
    }

    [Fact]
    public void FileLevelSelection_SetOnDiffsPage_IsVisibleOnTutorialPage()
    {
        var promptResultStore = new FakePromptResultStore();
        promptResultStore.SetResult(
            "tutorial",
            [new ProposedFileChangeViewModel("a.txt", "proposed", "original")],
            "/tmp");

        var tutorialViewModel = new TutorialViewModel(promptResultStore, new FakeApplyOrchestrationService(), new FakeApplyHistoryLog(), new FakeDialogService(), new FakeLlmRunner(), new PromptTemplateProvider(), new FakeFileSavePickerService(), new FakeLocalSettingsService(), new FakeBuildTestRunner());
        var diffsViewModel = new DiffsViewModel(promptResultStore, new FakeNavigationService());
        diffsViewModel.OnNavigatedTo(null!);

        diffsViewModel.Files[0].IsSelected = false;

        Assert.False(tutorialViewModel.Files[0].IsSelected);
    }
}
