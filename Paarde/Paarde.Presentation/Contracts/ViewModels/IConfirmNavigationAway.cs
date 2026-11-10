namespace Paarde.Presentation.Contracts.ViewModels;

// Opt-in for a page ViewModel that holds state the user could lose by navigating away (e.g.
// unsaved editor edits). NavigationService checks this from Frame.Navigating - the only
// FluentAvalonia hook that fires *before* the frame's content changes and can still cancel the
// navigation; INavigationAware.OnNavigatedFrom runs only after the fact, too late to stop
// anything.
public interface IConfirmNavigationAway
{
    bool HasUnsavedChanges { get; }
}
