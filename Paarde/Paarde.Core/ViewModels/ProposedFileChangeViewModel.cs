using System.ComponentModel;

using Paarde.Core.Helpers;
using Paarde.Core.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paarde.Core.ViewModels;

// Mutable, observable replacement for the old init-only ProposedFileChange record. Tracks the file's
// three content states - OriginalContent (on disk today), ProposedContent (the LLM's output), and
// EditedContent (what the user has changed it to, starting equal to ProposedContent) - so an in-app
// editor (Epic 2.2) can diverge from the model's output without losing track of either source. Lives
// in Core rather than the Avalonia project because PromptResultStore (also Core) holds the shared
// graph of these that both the Tutorial and Diffs pages bind to - the same instance flows into both,
// so DiffLines (computed, not settable) recomputes itself whenever OriginalContent or EditedContent
// change, keeping the Diffs page in sync with edits made on the Tutorial page without either page
// having to ask the other to refresh.
public partial class ProposedFileChangeViewModel : ObservableObject
{
    public string FilePath { get; }

    public string ProposedContent { get; }

    private IReadOnlyList<ProposedHunk> hunks = Array.Empty<ProposedHunk>();

    // Fallback selection state for files with no resolved hunks (the parser found no tutorial
    // steps, or none of their snippets could be located in the content) - there's nothing to roll
    // up from, so IsSelected just means "apply this whole file" directly. Defaults to true to match
    // today's apply-everything behavior.
    private bool noHunksSelected = true;

    // init rather than a plain [ObservableProperty] field, since the set of steps a response
    // parses out doesn't change after construction - but it still needs to run the same
    // matching logic as OnDiffHunksChanged below, because it's assigned via an object initializer
    // (ClaudeResponseParser.Parse), which runs after the constructor - and therefore after
    // DiffHunks already has its initial value. Also subscribes to each hunk's PropertyChanged so
    // IsSelected/SelectedHunkCount/SelectionSummary stay correct as the user checks/unchecks steps.
    public IReadOnlyList<ProposedHunk> Hunks
    {
        get => hunks;
        init
        {
            hunks = value;
            UpdateMatchedDiffHunks();

            foreach (var hunk in hunks)
            {
                hunk.PropertyChanged += OnHunkPropertyChanged;
            }
        }
    }

    private void OnHunkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProposedHunk.IsSelected))
        {
            return;
        }

        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(SelectedHunkCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(HasAnySelectedChange));
    }

    // Tri-state rollup of every hunk's IsSelected: true when all are selected, false when none are,
    // null (indeterminate) when mixed. Setting it fans out to every hunk. For a file with no hunks
    // at all, reads/writes the plain whole-file fallback instead since there's nothing to roll up.
    public bool? IsSelected
    {
        get
        {
            if (hunks.Count == 0)
            {
                return noHunksSelected;
            }

            if (hunks.All(h => h.IsSelected))
            {
                return true;
            }

            return hunks.All(h => !h.IsSelected) ? false : null;
        }
        set
        {
            if (hunks.Count == 0)
            {
                // A three-state CheckBox can land on null by repeated clicking even though nothing
                // here ever sets it programmatically; treat that the same as selecting everything
                // rather than leaving the fallback in an unselectable state.
                noHunksSelected = value ?? true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectionSummary));
                OnPropertyChanged(nameof(HasAnySelectedChange));
                return;
            }

            foreach (var hunk in hunks)
            {
                hunk.IsSelected = value ?? true;
            }
        }
    }

    public int SelectedHunkCount => hunks.Count(h => h.IsSelected);

    public bool HasSelectableHunks => hunks.Count > 0;

    public string SelectionSummary => hunks.Count == 0
        ? (noHunksSelected ? "Whole file" : "Excluded")
        : $"{SelectedHunkCount}/{hunks.Count} selected";

    // Whether applying this file would actually write anything - false means ApplyOrchestrationService
    // skips it entirely rather than writing a no-op (or, for the no-hunks fallback, the original
    // content back over itself).
    public bool HasAnySelectedChange => hunks.Count == 0 ? noHunksSelected : hunks.Any(h => h.IsSelected);

    private bool CanChangeHunkSelection() => hunks.Count > 0;

    [RelayCommand(CanExecute = nameof(CanChangeHunkSelection))]
    private void SelectAllHunks()
    {
        foreach (var hunk in hunks)
        {
            hunk.IsSelected = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeHunkSelection))]
    private void SelectNoHunks()
    {
        foreach (var hunk in hunks)
        {
            hunk.IsSelected = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeHunkSelection))]
    private void InvertHunkSelection()
    {
        foreach (var hunk in hunks)
        {
            hunk.IsSelected = !hunk.IsSelected;
        }
    }

    [ObservableProperty]
    private string originalContent;

    [ObservableProperty]
    private string editedContent;

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private IReadOnlyList<DiffLine> diffLines = Array.Empty<DiffLine>();

    // Unified-diff-style grouping of DiffLines (see DiffBuilder.GroupIntoHunks), kept in sync with
    // DiffLines rather than recomputed on demand so Hunks' MatchedDiffHunks (below) can be updated
    // reactively via OnDiffHunksChanged instead of every consumer having to remember to re-match.
    [ObservableProperty]
    private IReadOnlyList<DiffHunk> diffHunks = Array.Empty<DiffHunk>();

    public ProposedFileChangeViewModel(string filePath, string proposedContent, string originalContent = "")
    {
        FilePath = filePath;
        ProposedContent = proposedContent;
        this.originalContent = originalContent;
        editedContent = proposedContent;
        diffLines = DiffBuilder.BuildLineDiff(originalContent, proposedContent);
        diffHunks = DiffBuilder.GroupIntoHunks(diffLines);
    }

    partial void OnEditedContentChanged(string value)
    {
        IsDirty = value != ProposedContent;
        DiffLines = DiffBuilder.BuildLineDiff(OriginalContent, value);
        RevertToOriginalCommand.NotifyCanExecuteChanged();
    }

    partial void OnOriginalContentChanged(string value)
    {
        DiffLines = DiffBuilder.BuildLineDiff(value, EditedContent);
        RevertToOriginalCommand.NotifyCanExecuteChanged();
    }

    partial void OnDiffLinesChanged(IReadOnlyList<DiffLine> value) => DiffHunks = DiffBuilder.GroupIntoHunks(value);

    partial void OnDiffHunksChanged(IReadOnlyList<DiffHunk> value) => UpdateMatchedDiffHunks();

    private void UpdateMatchedDiffHunks()
    {
        foreach (var hunk in hunks)
        {
            hunk.MatchedDiffHunks = ProposedHunkMatcher.FindMatches(DiffHunks, hunk.Location);

            // The diff this hunk matches against just changed (new edit, or OriginalContent refreshed
            // from disk), so whatever was last applied no longer necessarily corresponds to it -
            // same reasoning as ProposedHunk.OnIsSelectedChanged, just triggered from this side.
            hunk.IsApplied = false;
        }
    }

    // Composes the content that applying this file right now would actually write: for a file with no
    // resolved hunks, the whole-file fallback (EditedContent if selected, otherwise OriginalContent
    // untouched); otherwise DiffBuilder.ComposeContent over DiffLines/DiffHunks, selecting a DiffHunk
    // if any ProposedHunk (tutorial step) matched to it is selected - matching ANY rather than ALL,
    // since GroupIntoHunks can merge two steps' changes into one DiffHunk (when they're close enough
    // together that their context windows overlap) and there's no way to apply half of a merged hunk.
    // A DiffHunk nothing matched to (an unresolved step location, or a change the parser didn't tie to
    // any step at all) has no checkbox to exclude it with, so it defaults to included.
    public string BuildSelectedContent()
    {
        if (hunks.Count == 0)
        {
            return noHunksSelected ? EditedContent : OriginalContent;
        }

        return DiffBuilder.ComposeContent(DiffLines, DiffHunks, IsDiffHunkSelected);
    }

    private bool IsDiffHunkSelected(DiffHunk diffHunk)
    {
        var matchingSteps = hunks.Where(h => h.MatchedDiffHunks.Contains(diffHunk)).ToList();
        return matchingSteps.Count == 0 || matchingSteps.Any(h => h.IsSelected);
    }

    // The inline editor's gutter Accept/Reject buttons are an alternate entry point into
    // the same per-step selection the Tutorial page's checkboxes drive, not a second selection
    // model - so this just finds every ProposedHunk whose matched diff hunks include diffHunk and
    // flips its IsSelected, exactly what checking/unchecking that step's checkbox already does. A
    // diffHunk no step's location resolved to (unmatched) has nothing to flip; it stays included by
    // default per IsDiffHunkSelected above regardless.
    public void SetDiffHunkSelection(DiffHunk diffHunk, bool isSelected)
    {
        foreach (var hunk in hunks.Where(h => h.MatchedDiffHunks.Contains(diffHunk)))
        {
            hunk.IsSelected = isSelected;
        }
    }

    // Records that a write using the current selection just happened, so each step's IsApplied
    // reflects exactly what ended up on disk - a selected step is now applied, a deselected
    // one isn't, regardless of what either was before. Called by FileChangeApplyService.Apply right
    // after a successful write; a no-op for files with no resolved hunks (the fallback case has
    // nothing per-step to mark).
    public void MarkHunksAppliedFromSelection()
    {
        foreach (var hunk in hunks)
        {
            hunk.IsApplied = hunk.IsSelected;
        }
    }

    // Re-reads this file's current on-disk content and adopts it as the new diff baseline - used
    // when a conflict is detected (the file changed since this proposal's OriginalContent was
    // captured, T3.3.3) and the user chooses to re-diff rather than abandon or blindly overwrite it.
    // Reassigning OriginalContent recomputes DiffLines/DiffHunks (and, via UpdateMatchedDiffHunks,
    // clears every step's stale IsApplied) through the existing OnOriginalContentChanged hook.
    public void RefreshOriginalContent(string workingDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(workingDirectoryPath) || string.IsNullOrWhiteSpace(FilePath))
        {
            OriginalContent = string.Empty;
            return;
        }

        OriginalContent = FileContentReader.ReadOrEmpty(Path.Combine(workingDirectoryPath, FilePath));
    }

    // IsDirty only ever changes as a side effect of EditedContent changing (above), but hooking its
    // own partial method - rather than calling NotifyCanExecuteChanged from OnEditedContentChanged
    // directly - keeps RevertToProposedCommand correct even if something else ever sets IsDirty.
    partial void OnIsDirtyChanged(bool value) => RevertToProposedCommand.NotifyCanExecuteChanged();

    // Discards the user's edits, falling back to what the LLM proposed. Disabled once there's
    // nothing to discard (EditedContent already equals ProposedContent).
    private bool CanRevertToProposed() => IsDirty;

    [RelayCommand(CanExecute = nameof(CanRevertToProposed))]
    private void RevertToProposed() => EditedContent = ProposedContent;

    // Discards both the user's edits and the LLM's proposed change, falling back to the file's
    // content on disk today. Enabled whenever there's anything to discard back to - including the
    // not-dirty case where the user hasn't touched the editor but still wants to reject the whole
    // proposed change.
    private bool CanRevertToOriginal() => EditedContent != OriginalContent;

    [RelayCommand(CanExecute = nameof(CanRevertToOriginal))]
    private void RevertToOriginal() => EditedContent = OriginalContent;
}
