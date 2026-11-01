using System.IO;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Controls;
using Paarde.Avalonia.ViewModels;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Views;

public partial class ExplorerFileTabControl : UserControl
{
    // Epic 17, T17.1.3: display wrapper for ReferencesListBox - pure formatting (file name + line
    // number), so it lives here rather than on OpenFileTabViewModel or LspLocation itself.
    private sealed record ReferenceListItem(string DisplayText, LspLocation Location)
    {
        public override string ToString() => DisplayText;
    }

    public ExplorerFileTabControl()
    {
        InitializeComponent();

        // Epic 23: a cross-file jump opens or activates a different tab, which gets a brand-new
        // ExplorerFileTabControl instance (ExplorerView.axaml's TabControl.ContentTemplate) - same
        // "apply once this control is attached to the visual tree" shape
        // TutorialDetailControl.ApplyPendingStepTarget already needs for its own cross-file jumps.
        Loaded += (_, _) => ApplyPendingCaretTarget();

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
        };
        Editor.GotFocus += (_, _) => activeEditorContext.SetCaretPosition(Editor.CaretPosition);
        Editor.LostFocus += (_, _) => activeEditorContext.SetCaretPosition(null);
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
}
