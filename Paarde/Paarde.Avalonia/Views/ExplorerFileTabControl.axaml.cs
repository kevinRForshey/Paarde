using System.IO;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Paarde.Avalonia.Controls;
using Paarde.Avalonia.Helpers;
using Paarde.Core.Models;
using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;

namespace Paarde.Avalonia.Views;

public partial class ExplorerFileTabControl : UserControl
{
    // Epic 17, T17.1.3: display wrapper for ReferencesListBox - pure formatting (file name + line
    // number), so it lives here rather than on OpenFileTabViewModel or LspLocation itself.
    private sealed record ReferenceListItem(string DisplayText, LspLocation Location)
    {
        public override string ToString() => DisplayText;
    }

    // specs/unified-workbench T14 (R5/R7): one ExplorerFileTabControl instance per open tab
    // (Epic 23, T23.1.1), but DataContext is still assigned by the TabControl.ContentTemplate
    // after this constructor runs - tracked here so ScrollToHunkRequested can be unsubscribed if
    // DataContext is ever reassigned, same caution TutorialDetailControl takes with its own owner
    // reference even though it's likewise expected to be set once.
    private OpenFileTabViewModel? _viewModel;

    // specs/editor-intelligence R4: same ResizablePaneHelper convention ExplorerView.axaml.cs's
    // own _paneHelper already uses for its tree pane, persisted per this control rather than
    // per-page since every open tab gets its own OutlinePanelGrid.
    private readonly ResizablePaneHelper _outlinePaneHelper;

    public ExplorerFileTabControl()
    {
        InitializeComponent();

        _outlinePaneHelper = new ResizablePaneHelper(OutlinePanelGrid.ColumnDefinitions[0], App.GetService<ILocalSettingsService>(), WorkspaceLayoutSettingsKeys.OutlinePanelWidth, defaultWidth: 200);
        Loaded += async (_, _) => await _outlinePaneHelper.RestoreAsync();
        OutlineSplitter.DragCompleted += (_, _) => _outlinePaneHelper.OnSplitterDragCompleted();

        // Epic 23: a cross-file jump opens or activates a different tab, which gets a brand-new
        // ExplorerFileTabControl instance (ExplorerView.axaml's TabControl.ContentTemplate) - same
        // "apply once this control is attached to the visual tree" shape
        // TutorialDetailControl.ApplyPendingStepTarget already needs for its own cross-file jumps.
        Loaded += (_, _) => ApplyPendingCaretTarget();

        // specs/unified-workbench T14 (R7): mirrors TutorialDetailControl.ApplyPendingStepTarget's
        // jump-to-target feature, adapted from an ItemsControl row index (DiffsDetailControl's
        // ScrollToHunk) to CodeEditor's own caret API, since this editor shows the file as running
        // text rather than a row-per-DiffLine list.
        DataContextChanged += OnDataContextChanged;

        // T13.1.3/T13.2.2 (same handler shape as TutorialDetailControl.OnDiffHunkActionRequested):
        // the gutter's Accept/Reject buttons forward into PendingChange's own hunk selection -
        // there's nothing to do on a plain tab with no PendingChange.
        Editor.DiffHunkActionRequested += OnDiffHunkActionRequested;

        // Epic 15: Editor itself has no ILlmRunner - this control's DataContext
        // (OpenFileTabViewModel) owns the ghost-text/completion round trip directly now, since
        // both only ever need this one file's own state.
        Editor.GhostTextRequested += async (_, e) =>
        {
            if (DataContext is OpenFileTabViewModel tab)
            {
                await tab.RequestGhostTextCompletionAsync(e.TextBeforeCaret, e.TextAfterCaret);
            }
        };

        // Epic 17, T17.1.3: a cross-file jump only reflects into Editor.Text once Content's
        // binding runs, so TextChanged is the reliable place to apply a pending caret target -
        // same "react to TextChanged" pattern TutorialDetailControl already established for its
        // own pending-step-target pull. The direct call right after each await below covers the
        // same-file case, where Editor.Text never changes and TextChanged never fires.
        Editor.TextChanged += (_, _) => ApplyPendingCaretTarget();

        // Epic 23: GoToDefinition/FindReferences still need the page-level ExplorerViewModel
        // (cross-file navigation can open or activate a different tab), so they're forwarded up
        // via the same ancestor lookup TutorialDetailControl already uses to reach
        // TutorialViewModel.
        Editor.GoToDefinitionRequested += async (_, e) =>
        {
            if (DataContext is not OpenFileTabViewModel tab || this.FindAncestorOfType<ExplorerView>()?.ViewModel is not { } explorerViewModel)
            {
                return;
            }

            await explorerViewModel.GoToDefinitionAsync(tab.FilePath, e.Position);
            ApplyPendingCaretTarget();
        };

        Editor.FindReferencesRequested += async (_, e) =>
        {
            if (DataContext is not OpenFileTabViewModel tab || this.FindAncestorOfType<ExplorerView>()?.ViewModel is not { } explorerViewModel)
            {
                return;
            }

            var locations = await explorerViewModel.FindReferencesAsync(tab.FilePath, e.Position);
            ShowReferencesFlyout(locations);
        };

        // specs/editor-autocomplete T4: same "Editor just reports, ViewModel decides" split as
        // GhostTextRequested above - the round trip here hits the language server directly rather
        // than ILlmRunner. CompletionItems is bound in XAML now, so there's nothing to assign back
        // imperatively once the request completes.
        Editor.CompletionRequested += async (_, e) =>
        {
            if (DataContext is OpenFileTabViewModel tab)
            {
                await tab.RequestCompletionAsync(e.Position);
            }
        };

        // specs/editor-intelligence R3: same "Editor just reports, ViewModel decides" split as
        // GhostTextRequested/CompletionRequested above - DocumentSymbols is bound in XAML, so there's
        // nothing to assign back imperatively once the request completes.
        Editor.DocumentSymbolsRequested += async (_, _) =>
        {
            if (DataContext is OpenFileTabViewModel tab)
            {
                await tab.RequestDocumentSymbolsAsync();
            }
        };

        // specs/editor-intelligence R6: same "Editor just reports, ViewModel decides" split as
        // CompletionRequested above - HoverSuggestion is bound in XAML, so there's nothing to
        // assign back imperatively once the request completes.
        Editor.HoverRequested += async (_, e) =>
        {
            if (DataContext is OpenFileTabViewModel tab)
            {
                await tab.RequestHoverAsync(e.Position);
            }
        };

        // specs/ide-workbench-shell, R3.5: only the editor that currently has focus should drive
        // the status bar's caret readout - every ExplorerFileTabControl instance (one per open
        // tab) shares the same IActiveEditorContext singleton with ExplorerView and
        // TutorialDetailControl, so each only reports while its own Editor is focused and clears
        // on LostFocus rather than leaving a stale position from whichever tab had focus last.
        var activeEditorContext = App.GetService<IActiveEditorContext>();
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == CodeEditor.CaretPositionProperty && Editor.IsFocused)
            {
                activeEditorContext.SetCaretPosition(Editor.CaretPosition);
            }

            // specs/editor-intelligence R7: same "only report while this Editor is focused" rule
            // as CaretPosition above - DocumentSymbols is re-reported on every settled-edit
            // refresh (T3's debounce) so the symbol picker never filters against a stale outline.
            if (e.Property == CodeEditor.DocumentSymbolsProperty && Editor.IsFocused && DataContext is OpenFileTabViewModel tab)
            {
                activeEditorContext.SetActiveDocument(tab.FilePath, Editor.DocumentSymbols);
            }
        };
        Editor.GotFocus += (_, _) =>
        {
            activeEditorContext.SetCaretPosition(Editor.CaretPosition);
            if (DataContext is OpenFileTabViewModel tab)
            {
                activeEditorContext.SetActiveDocument(tab.FilePath, Editor.DocumentSymbols);
            }

            activeEditorContext.NavigationRequested += JumpCaretTo;
        };
        Editor.LostFocus += (_, _) =>
        {
            activeEditorContext.SetCaretPosition(null);
            activeEditorContext.SetActiveDocument(null, null);
            activeEditorContext.NavigationRequested -= JumpCaretTo;
        };
    }

    // specs/editor-intelligence R7: Go to Symbol only ever jumps within the file that's already
    // focused (SymbolQuickPickViewModel filters that same tab's own DocumentSymbols), so this
    // moves the caret directly rather than going through ExplorerViewModel's cross-file
    // NavigateToLocation/TakePendingCaretTarget path T4-T6 use for jumps that can open another tab.
    private void JumpCaretTo(LspPosition position)
    {
        if (Editor.Document is null)
        {
            return;
        }

        try
        {
            var offset = Editor.Document.GetOffset(position.Line + 1, position.Character + 1);
            Editor.CaretOffset = Math.Clamp(offset, 0, Editor.Document.TextLength);
            Editor.TextArea.Caret.BringCaretToView();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Stale position from a file that's since changed since the picker's list was built -
            // nothing sensible to do but skip it.
        }
    }

    private void ShowReferencesFlyout(IReadOnlyList<LspLocation> locations)
    {
        if (locations.Count == 0)
        {
            return;
        }

        ReferencesListBox.ItemsSource = locations
            .Select(location => new ReferenceListItem($"{Path.GetFileName(location.FilePath)}:{location.Position.Line + 1}", location))
            .ToArray();

        FlyoutBase.ShowAttachedFlyout(Editor);
    }

    // specs/unified-workbench T13 (R5): same ancestor lookup GoToDefinitionRequested/
    // FindReferencesRequested above already use to reach ExplorerViewModel - AssistantPanel lives
    // there, not on OpenFileTabViewModel, since every tab shares the one docked panel.
    private void OnOpenInReviewButtonClick(object? sender, RoutedEventArgs e)
    {
        this.FindAncestorOfType<ExplorerView>()?.ViewModel?.AssistantPanel.ShowReview();
    }

    private void OnReferenceSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (ReferencesListBox.SelectedItem is not ReferenceListItem item)
        {
            return;
        }

        if (this.FindAncestorOfType<ExplorerView>()?.ViewModel is { } explorerViewModel)
        {
            explorerViewModel.NavigateToLocation(item.Location);
            ApplyPendingCaretTarget();
        }

        FlyoutBase.GetAttachedFlyout(Editor)?.Hide();
        ReferencesListBox.SelectedItem = null;
    }

    // specs/editor-intelligence R4: clicking an outline node jumps the caret via the same
    // NavigateToLocation/ApplyPendingCaretTarget path OnReferenceSelected above already uses -
    // deliberately not a CodeEditor-owned SymbolNavigationRequested event, since the outline panel
    // isn't one of CodeEditor's own children and can't raise an event CodeEditor declares.
    private void OnOutlineSymbolSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (OutlineTreeView.SelectedItem is not LspDocumentSymbol symbol)
        {
            return;
        }

        if (DataContext is OpenFileTabViewModel tab && this.FindAncestorOfType<ExplorerView>()?.ViewModel is { } explorerViewModel)
        {
            explorerViewModel.NavigateToLocation(new LspLocation(tab.FilePath, symbol.SelectionRange.Start));
            ApplyPendingCaretTarget();
        }

        OutlineTreeView.SelectedItem = null;
    }

    // specs/editor-intelligence R5: same NavigateToLocation/ApplyPendingCaretTarget path
    // OnOutlineSymbolSelected above already uses - clicking a breadcrumb segment jumps the caret
    // to that symbol's own selection range.
    private void OnBreadcrumbSegmentClicked(object? sender, LspDocumentSymbol symbol)
    {
        if (DataContext is OpenFileTabViewModel tab && this.FindAncestorOfType<ExplorerView>()?.ViewModel is { } explorerViewModel)
        {
            explorerViewModel.NavigateToLocation(new LspLocation(tab.FilePath, symbol.SelectionRange.Start));
            ApplyPendingCaretTarget();
        }
    }

    private void ApplyPendingCaretTarget()
    {
        if (this.FindAncestorOfType<ExplorerView>()?.ViewModel.TakePendingCaretTarget() is not { } position || Editor.Document is null)
        {
            return;
        }

        try
        {
            var offset = Editor.Document.GetOffset(position.Line + 1, position.Character + 1);
            Editor.CaretOffset = Math.Clamp(offset, 0, Editor.Document.TextLength);
            Editor.TextArea.Caret.BringCaretToView();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Stale position from a file that's since changed on disk - nothing sensible to do but skip it.
        }
    }

    // specs/unified-workbench T14 (R5): forwards the gutter's Accept/Reject into the same
    // PendingChange.SetDiffHunkSelection selection model the step-card checkboxes in Review drive,
    // so accepting/rejecting from either place stays in sync.
    private void OnDiffHunkActionRequested(object? sender, DiffHunkAcceptRejectEventArgs e)
    {
        if (DataContext is OpenFileTabViewModel tab)
        {
            tab.PendingChange?.SetDiffHunkSelection(e.Hunk, e.Accept);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollToHunkRequested -= OnScrollToHunkRequested;
        }

        _viewModel = DataContext as OpenFileTabViewModel;

        if (_viewModel is not null)
        {
            _viewModel.ScrollToHunkRequested += OnScrollToHunkRequested;
        }
    }

    // specs/unified-workbench T14 (R7): DiffHunkGutterMargin.RebuildEntries already establishes
    // "ProposedStartLine, clamped to at least 1" as this editor's line-number convention for a
    // hunk (it can be a 0-based insertion point per DiffHunk's own doc comment) - reused here
    // rather than introducing a second interpretation of the same field.
    private void OnScrollToHunkRequested(DiffHunk hunk)
    {
        if (Editor.Document is null)
        {
            return;
        }

        var anchorLine = Math.Min(Math.Max(hunk.ProposedStartLine, 1), Editor.Document.LineCount);

        try
        {
            var offset = Editor.Document.GetOffset(anchorLine, 1);
            Editor.CaretOffset = Math.Clamp(offset, 0, Editor.Document.TextLength);
            Editor.TextArea.Caret.BringCaretToView();
        }
        catch (ArgumentOutOfRangeException)
        {
            // Stale hunk from a file that's since changed - nothing sensible to do but skip it.
        }
    }
}
