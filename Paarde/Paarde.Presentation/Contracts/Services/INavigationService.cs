namespace Paarde.Presentation.Contracts.Services;

// specs/presentation-extraction R2.4: the UI-framework-agnostic half of navigation that view
// models depend on. Page keys are view-model type full names (PageService maps them to views).
// The Avalonia-only Frame wiring and Navigated event live on IFrameNavigationService.
public interface INavigationService
{
    bool CanGoBack
    {
        get;
    }

    bool NavigateTo(string pageKey, object? parameter = null, bool clearNavigation = false);

    bool GoBack();
}
