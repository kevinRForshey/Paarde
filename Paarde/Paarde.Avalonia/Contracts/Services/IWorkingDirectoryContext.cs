namespace Paarde.Avalonia.Contracts.Services;

// specs/build-and-run/design.md's "working-directory-changed pub/sub hook": RunPanelViewModel is
// a DI singleton, not a page ViewModel, so it has no INavigationAware.OnNavigatedTo to re-read the
// working directory from - this is the one place that gets told whenever PromptViewModel (where
// the working directory is picked) changes it, without PromptViewModel needing to know
// RunPanelViewModel exists.
public interface IWorkingDirectoryContext
{
    event Action<string>? Changed;

    void SetCurrent(string workingDirectoryPath);
}
