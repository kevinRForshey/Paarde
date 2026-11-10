using Paarde.Core.Models;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Paarde.Core.ViewModels;

// Wraps a single parsed tutorial step as a selectable unit of change, carrying the resolved line
// range (if any) its snippet produces in the file. IsSelected drives the per-step checkbox
// and the owning ProposedFileChangeViewModel's tri-state file-level rollup, and (via
// ProposedFileChangeViewModel.BuildSelectedContent) determines whether this step's diff hunk(s) end
// up in what actually gets written.
public partial class ProposedHunk : ObservableObject
{
    public string Title { get; }

    public string Explanation { get; }

    public string Snippet { get; }

    public HunkLocation? Location { get; }

    [ObservableProperty]
    private bool isSelected = true;

    // Whether this step's change is currently reflected in what's on disk. Only ever set to true by
    // ProposedFileChangeViewModel.MarkHunksAppliedFromSelection, right after a write that included
    // this step's selection state - never set directly from the UI. T3.3.2: this exists so "applied"
    // means "this step's current selection is what was actually written", not the old
    // MarkAllStepsApplied() workaround that marked every step applied just because any one of them
    // was clicked.
    [ObservableProperty]
    private bool isApplied;

    public ProposedHunk(string title, string explanation, string snippet, HunkLocation? location)
    {
        Title = title;
        Explanation = explanation;
        Snippet = snippet;
        Location = location;
    }

    // Changing the selection means whatever's currently on disk may no longer match what the user
    // wants - even if nothing else about the file changes, IsApplied would otherwise keep reporting
    // "applied" for a state that's now stale until the next apply re-establishes it.
    partial void OnIsSelectedChanged(bool value) => IsApplied = false;

    // The DiffHunk(s) (from ProposedFileChangeViewModel.DiffHunks) that this step's Location
    // overlaps - kept in sync by ProposedFileChangeViewModel whenever its DiffHunks are
    // recomputed. Empty when Location is unresolved or no diff hunk covers it (e.g. the step's
    // snippet turned out identical to what was already there).
    [ObservableProperty]
    private IReadOnlyList<DiffHunk> matchedDiffHunks = Array.Empty<DiffHunk>();
}
