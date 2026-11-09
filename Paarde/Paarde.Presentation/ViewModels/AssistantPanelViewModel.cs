using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Models;
using Paarde.Presentation.Services;
using Paarde.Core.Contracts.Services;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Paarde.Presentation.ViewModels;

// Epic 25, T2: composes the two existing page view models (Compose/Review) behind one docked
// "Assistant" panel living inside ExplorerView, instead of Main/Prompt/Tutorial/Diffs being
// separate navigation destinations. Registered AddSingleton (same as Compose/Review themselves,
// per design.md) so it's reachable from HistoryViewModel's Fork/Rerun without depending on
// ExplorerView ever having been constructed.
public partial class AssistantPanelViewModel : ObservableObject
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private int selectedTabIndex; // 0 = Compose, 1 = Review

    public PromptViewModel Compose { get; }

    public TutorialViewModel Review { get; }

    public AssistantPanelViewModel(PromptViewModel compose, TutorialViewModel review, IPromptResultStore promptResultStore, ILocalSettingsService localSettingsService, INavigationService navigationService)
    {
        Compose = compose;
        Review = review;
        _localSettingsService = localSettingsService;
        _navigationService = navigationService;

        // A finished run (successful or not - IPromptResultStore.Tutorial is set either way)
        // should put the narrative in front of the user without them having to click anything.
        // In EditorFocused mode ShowReview() alone is enough (the docked panel already lives on
        // whichever page is open); in FlowWizard mode (Epic 26, specs/interface-mode R8) there is
        // no docked panel, so this also has to navigate to the separate Review page.
        promptResultStore.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(IPromptResultStore.Tutorial))
            {
                ShowReview();

                var interfaceMode = await _localSettingsService.ReadSettingAsync<InterfaceMode?>(InterfaceModeSettingsKeys.Mode) ?? InterfaceMode.EditorFocused;
                if (interfaceMode == InterfaceMode.FlowWizard)
                {
                    _navigationService.NavigateTo(typeof(TutorialViewModel).FullName!);
                }
            }
        };
    }

    public void ShowCompose() => SelectedTabIndex = 0;

    public void ShowReview() => SelectedTabIndex = 1;
}
