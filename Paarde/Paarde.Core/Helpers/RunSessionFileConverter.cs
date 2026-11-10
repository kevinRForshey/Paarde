using Paarde.Core.Models;
using Paarde.Core.ViewModels;

namespace Paarde.Core.Helpers;

// Converts between RunSessionFile/RunSessionHunk (the persisted shape, T7.1.1) and
// ProposedFileChangeViewModel/ProposedHunk (the live, observable shape) in both directions.
// Shared by RunSessionStore (saving on RecordRun/Resave, restoring on RestoreLastSession, T7.1.2)
// and the History page's read-only reopen view, which otherwise would have needed its own
// copy of the restore direction.
public static class RunSessionFileConverter
{
    public static RunSessionFile ToRunSessionFile(ProposedFileChangeViewModel file) => new(
        file.FilePath,
        file.OriginalContent,
        file.ProposedContent,
        file.EditedContent,
        file.HasSelectableHunks ? true : file.IsSelected ?? true,
        file.Hunks.Select(ToRunSessionHunk).ToList());

    public static RunSessionHunk ToRunSessionHunk(ProposedHunk hunk) =>
        new(hunk.Title, hunk.Explanation, hunk.Snippet, hunk.Location, hunk.IsSelected);

    // Inverse of ToRunSessionFile/ToRunSessionHunk. WholeFileSelected is only applied when there are
    // no hunks - same "ignored otherwise" rule it was saved under.
    public static ProposedFileChangeViewModel ToProposedFileChangeViewModel(RunSessionFile file)
    {
        var viewModel = new ProposedFileChangeViewModel(file.FilePath, file.ProposedContent, file.OriginalContent)
        {
            EditedContent = file.EditedContent,
            Hunks = file.Hunks.Select(ToProposedHunk).ToList(),
        };

        if (file.Hunks.Count == 0)
        {
            viewModel.IsSelected = file.WholeFileSelected;
        }

        return viewModel;
    }

    public static ProposedHunk ToProposedHunk(RunSessionHunk hunk) =>
        new(hunk.Title, hunk.Explanation, hunk.Snippet, hunk.Location) { IsSelected = hunk.IsSelected };
}
