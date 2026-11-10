using System.Collections.ObjectModel;
using System.ComponentModel;

using Paarde.Core.ViewModels;

namespace Paarde.Core.Contracts.Services;

// INotifyPropertyChanged (Tutorial/WorkingDirectoryPath) plus the ObservableCollection on Files
// (CollectionChanged) are what let a consumer react to a new prompt result without having to be
// re-navigated to - today only PromptViewModel.OnNavigatedTo/Files pulls from this store on a
// navigation lifecycle event, but the store itself is a singleton that outlives any one page, so
// anything bound directly to it (e.g. a persistent tutorial-summary header) stays in sync.
public interface IPromptResultStore : INotifyPropertyChanged
{
    string Tutorial { get; }

    string WorkingDirectoryPath { get; }

    // The session id of the run that produced the current result, if any - lets an inline
    // follow-up action (Tutorial page's "explain further"/"refine this file") resume that same CLI
    // conversation instead of starting a fresh one. Null until a run has completed with a
    // session id.
    string? SessionId { get; }

    ObservableCollection<ProposedFileChangeViewModel> Files { get; }

    // sessionId is optional because the streaming (partial-result) call from PromptViewModel has no
    // session id yet - passing null there leaves SessionId as whatever it already was rather than
    // clearing it mid-run.
    void SetResult(string tutorial, IReadOnlyList<ProposedFileChangeViewModel> files, string workingDirectoryPath, string? sessionId = null);
}
