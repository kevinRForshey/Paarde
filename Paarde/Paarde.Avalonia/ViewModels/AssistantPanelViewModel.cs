using Paarde.Core.Contracts.Services;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Paarde.Avalonia.ViewModels;

// Epic 25, T2: composes the two existing page view models (Compose/Review) behind one docked
// "Assistant" panel living inside ExplorerView, instead of Main/Prompt/Tutorial/Diffs being
// separate navigation destinations. Registered AddSingleton (same as Compose/Review themselves,
// per design.md) so it's reachable from HistoryViewModel's Fork/Rerun without depending on
// ExplorerView ever having been constructed.
public partial class AssistantPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private int selectedTabIndex; // 0 = Compose, 1 = Review

    public PromptViewModel Compose { get; }

    public TutorialViewModel Review { get; }

    public AssistantPanelViewModel(PromptViewModel compose, TutorialViewModel review, IPromptResultStore promptResultStore)
    {
        Compose = compose;
        Review = review;

        // A finished run (successful or not - IPromptResultStore.Tutorial is set either way)
        // should put the narrative in front of the user without them having to click anything.
        promptResultStore.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IPromptResultStore.Tutorial))
            {
                ShowReview();
            }
        };
    }

    public void ShowCompose() => SelectedTabIndex = 0;

    public void ShowReview() => SelectedTabIndex = 1;
}
