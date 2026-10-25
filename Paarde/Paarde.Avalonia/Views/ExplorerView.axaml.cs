using System.IO;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Helpers;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.ViewModels;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Views;

public partial class ExplorerView : UserControl
{
    // Epic 17, T17.1.3: display wrapper for ReferencesListBox - pure formatting (file name + line
    // number), so it lives here rather than on ExplorerViewModel or LspLocation itself.
    private sealed record ReferenceListItem(string DisplayText, LspLocation Location)
    {
        public override string ToString() => DisplayText;
    }

    public ExplorerViewModel ViewModel
    {
        get;
    }

    private readonly ResizablePaneHelper _paneHelper;

    public ExplorerView()
    {
        ViewModel = App.GetService<ExplorerViewModel>();
        DataContext = ViewModel;
        InitializeComponent();

        _paneHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[0], App.GetService<ILocalSettingsService>(), WorkspaceLayoutSettingsKeys.ExplorerTreeWidth, defaultWidth: 300);
        Loaded += async (_, _) => await _paneHelper.RestoreAsync();
        TreeSplitter.DragCompleted += (_, _) => _paneHelper.OnSplitterDragCompleted();

        // Epic 15: Editor itself has no ILlmRunner - it just reports where the caret is, and
        // ViewModel (which already owns _llmRunner for ExplainSelectedFileCommand) decides what to
        // do with that.
        Editor.GhostTextRequested += async (_, e) => await ViewModel.RequestGhostTextCompletionAsync(e.TextBeforeCaret, e.TextAfterCaret);

        // Epic 17, T17.1.3: a cross-file jump only reflects into Editor.Text once
        // SelectedFileContent's binding runs, so TextChanged is the reliable place to apply a
        // pending caret target - same "react to TextChanged" pattern TutorialDetailControl already
        // established for its own pending-step-target pull. The direct call right after each await
        // below covers the same-file case, where Editor.Text never changes and TextChanged never
        // fires.
        Editor.TextChanged += (_, _) => ApplyPendingCaretTarget();

        Editor.GoToDefinitionRequested += async (_, e) =>
        {
            await ViewModel.GoToDefinitionAsync(e.Position);
            ApplyPendingCaretTarget();
        };

        Editor.FindReferencesRequested += async (_, e) =>
        {
            var locations = await ViewModel.FindReferencesAsync(e.Position);
            ShowReferencesFlyout(locations);
        };

        // specs/editor-autocomplete T4: same "Editor just reports, ViewModel decides" split as
        // GhostTextRequested above - the round trip here hits the language server directly rather
        // than ILlmRunner.
        Editor.CompletionRequested += async (_, e) =>
        {
            await ViewModel.RequestCompletionAsync(e.Position);
            Editor.CompletionItems = ViewModel.CompletionItems;
        };
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

        ViewModel.NavigateToLocation(item.Location);
        ApplyPendingCaretTarget();
        FlyoutBase.GetAttachedFlyout(Editor)?.Hide();
        ReferencesListBox.SelectedItem = null;
    }

    private void ApplyPendingCaretTarget()
    {
        if (ViewModel.TakePendingCaretTarget() is not { } position || Editor.Document is null)
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
