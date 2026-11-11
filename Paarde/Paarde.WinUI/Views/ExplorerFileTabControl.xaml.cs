using System.ComponentModel;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

using Paarde.Core.Models;
using Paarde.Core.ViewModels;
using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;
using Paarde.WinUI.Controls;
using Paarde.WinUI.Helpers;

namespace Paarde.WinUI.Views;

public sealed partial class ExplorerFileTabControl : UserControl
{
    // Display wrapper for the references list - file name + line number.
    private sealed record ReferenceListItem(string DisplayText, LspLocation Location)
    {
        public override string ToString() => DisplayText;
    }

    // specs/ide-workbench-shell R3.5 / editor-intelligence R7: the editor that last had focus owns
    // IActiveEditorContext (the status bar's caret readout, Go to Symbol's outline and jump). Losing
    // focus only clears the caret readout, because opening Go to Symbol from inside the editor moves
    // focus to the popup before the pick is made. Paarde.Avalonia's ExplorerFileTabControl does the same.
    private static ExplorerFileTabControl? s_activeEditorOwner;

    private readonly IActiveEditorContext _activeEditorContext;
    private readonly ResizablePaneHelper _outlinePaneHelper;

    // The outline is refreshed once edits settle - same 400 ms window
    // as Avalonia's CodeEditor.DocumentSymbolsRequested.
    private readonly DispatcherQueueTimer _documentSymbolsTimer;

    // Every file this control's editor has a Monaco model for, so closed tabs' models are disposed.
    private readonly HashSet<string> _documentPaths = new(StringComparer.Ordinal);

    private ProposedFileChangeViewModel? _pendingChange;

    // The last text the editor reported, so the view model echoing it back isn't pushed to Monaco.
    private string? _lastTextFromEditor;

    public OpenFileTabViewModel? ViewModel { get; private set; }

    // Set by ExplorerView: GoToDefinition/FindReferences/NavigateToLocation and the Assistant panel
    // live on the page's view model (Avalonia reaches it with FindAncestorOfType).
    public ExplorerViewModel? Explorer { get; set; }

    // The editor got focus - ExplorerView makes this group's tab the selected one.
    public event EventHandler? EditorActivated;

    // Keys the editor's WebView2 swallowed; ExplorerView handles them.
    public event Action<string>? ShortcutForwarded;

    public ExplorerFileTabControl()
    {
        InitializeComponent();

        _activeEditorContext = App.GetService<IActiveEditorContext>();

        _outlinePaneHelper = new ResizablePaneHelper(OutlinePanelGrid.ColumnDefinitions[0], App.GetService<ILocalSettingsService>(), WorkspaceLayoutSettingsKeys.OutlinePanelWidth, defaultWidth: 200);
        _outlinePaneHelper.AttachSplitter(OutlineSplitter);

        _documentSymbolsTimer = DispatcherQueue.CreateTimer();
        _documentSymbolsTimer.Interval = TimeSpan.FromMilliseconds(400);
        _documentSymbolsTimer.IsRepeating = false;
        _documentSymbolsTimer.Tick += async (_, _) =>
        {
            if (ViewModel is { } tab)
            {
                await tab.RequestDocumentSymbolsAsync();
            }
        };

        Editor.TextEdited += OnEditorTextEdited;
        Editor.DiffHunkActionRequested += (_, e) => ViewModel?.PendingChange?.SetDiffHunkSelection(e.Hunk, e.Accept);
        Editor.GoToDefinitionRequested += OnGoToDefinitionRequested;
        Editor.FindReferencesRequested += OnFindReferencesRequested;
        Editor.ShortcutForwarded += gesture => ShortcutForwarded?.Invoke(gesture);
        Editor.EditorGotFocus += (_, _) =>
        {
            TakeActiveEditor();
            EditorActivated?.Invoke(this, EventArgs.Empty);
        };
        Editor.EditorLostFocus += (_, _) =>
        {
            if (s_activeEditorOwner == this)
            {
                _activeEditorContext.SetCaretPosition(null);
            }
        };
        Editor.RegisterPropertyChangedCallback(CodeEditor.CaretPositionProperty, (_, _) =>
        {
            if (Editor.HasEditorFocus && s_activeEditorOwner == this)
            {
                _activeEditorContext.SetCaretPosition(Editor.CaretPosition);
            }
        });

        // Epic 15/17, editor-autocomplete, editor-intelligence R6: the tab's view model makes each
        // round trip and publishes the answer in a property; each provider clears that property
        // first so a failed request can't hand back the previous answer.
        Editor.GhostTextProvider = async (textBeforeCaret, textAfterCaret) =>
        {
            if (ViewModel is not { } tab)
            {
                return null;
            }

            tab.GhostTextSuggestion = null;
            await tab.RequestGhostTextCompletionAsync(textBeforeCaret, textAfterCaret);
            return tab.GhostTextSuggestion;
        };
        Editor.CompletionProvider = async position =>
        {
            if (ViewModel is not { } tab)
            {
                return null;
            }

            tab.CompletionItems = null;
            await tab.RequestCompletionAsync(position);
            return tab.CompletionItems;
        };
        Editor.HoverProvider = async position =>
        {
            if (ViewModel is not { } tab)
            {
                return null;
            }

            tab.HoverSuggestion = null;
            await tab.RequestHoverAsync(position);
            return tab.HoverSuggestion;
        };

        Loaded += async (_, _) =>
        {
            await _outlinePaneHelper.RestoreAsync();
            if (App.MainWindow is { } window)
            {
                window.Activated += OnWindowActivated;
            }
        };
        Unloaded += (_, _) =>
        {
            if (App.MainWindow is { } window)
            {
                window.Activated -= OnWindowActivated;
            }

            _documentSymbolsTimer.Stop();
            ReleaseActiveEditor();
            ShowTab(null);
        };
    }

    // Avalonia binds this control's DataContext per tab; here ExplorerView calls this whenever the
    // group's shown tab changes.
    public void ShowTab(OpenFileTabViewModel? tab)
    {
        if (ReferenceEquals(tab, ViewModel))
        {
            return;
        }

        if (ViewModel is { } previous)
        {
            previous.PropertyChanged -= OnTabPropertyChanged;
            previous.ScrollToHunkRequested -= OnScrollToHunkRequested;
        }

        SetPendingChange(null);
        _documentSymbolsTimer.Stop();
        _lastTextFromEditor = null;

        ViewModel = tab;
        Bindings.Update();

        if (tab is null)
        {
            Editor.ClearDocument();
            if (s_activeEditorOwner == this)
            {
                _activeEditorContext.SetActiveDocument(null, null);
            }

            return;
        }

        _documentPaths.Add(tab.FilePath);
        Editor.ShowDocument(tab.FilePath, tab.EffectiveContent);
        Editor.SetDiagnostics(tab.FilePath, tab.Diagnostics);
        SetPendingChange(tab.PendingChange);
        tab.PropertyChanged += OnTabPropertyChanged;
        tab.ScrollToHunkRequested += OnScrollToHunkRequested;

        // Avalonia asks for the outline when a tab's editor first gets
        // its file; here the outline is kept on the tab, so only a tab without one asks.
        if (tab.DocumentSymbols is null)
        {
            _ = tab.RequestDocumentSymbolsAsync();
        }

        if (s_activeEditorOwner == this)
        {
            _activeEditorContext.SetActiveDocument(tab.FilePath, tab.DocumentSymbols);
        }

        // A cross-file jump (F12, references, outline) activates the target tab and leaves the
        // caret target behind; only the tab that's actually selected may take it.
        if (ReferenceEquals(Explorer?.SelectedTab, tab))
        {
            ApplyPendingCaretTarget();
        }
    }

    // Drops the Monaco models of files that are no longer open in any tab.
    public void CloseDocumentsExcept(IEnumerable<string> openFilePaths)
    {
        var open = openFilePaths.ToHashSet(StringComparer.Ordinal);
        foreach (var path in _documentPaths.Where(path => !open.Contains(path)).ToList())
        {
            _documentPaths.Remove(path);
            Editor.CloseDocument(path);
        }
    }

    public void FocusEditor() => Editor.FocusEditor();

    public string FormatProposedChange(string? filePath) => $"Proposed change for {filePath}";

    private void OnEditorTextEdited(string filePath, string text)
    {
        if (ViewModel is not { } tab || tab.FilePath != filePath)
        {
            return;
        }

        _lastTextFromEditor = text;
        tab.EffectiveContent = text;

        _documentSymbolsTimer.Stop();
        _documentSymbolsTimer.Start();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is not { } tab)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(OpenFileTabViewModel.EffectiveContent):
                if (tab.EffectiveContent != _lastTextFromEditor)
                {
                    Editor.UpdateText(tab.FilePath, tab.EffectiveContent);
                }

                break;

            case nameof(OpenFileTabViewModel.Diagnostics):
                Editor.SetDiagnostics(tab.FilePath, tab.Diagnostics);
                break;

            case nameof(OpenFileTabViewModel.PendingChange):
                SetPendingChange(tab.PendingChange);
                break;

            case nameof(OpenFileTabViewModel.DocumentSymbols):
                // Go to Symbol never filters a stale outline.
                if (s_activeEditorOwner == this)
                {
                    _activeEditorContext.SetActiveDocument(tab.FilePath, tab.DocumentSymbols);
                }

                break;
        }
    }

    private void SetPendingChange(ProposedFileChangeViewModel? pendingChange)
    {
        if (_pendingChange is not null)
        {
            _pendingChange.PropertyChanged -= OnPendingChangePropertyChanged;
        }

        _pendingChange = pendingChange;

        if (pendingChange is not null)
        {
            pendingChange.PropertyChanged += OnPendingChangePropertyChanged;
        }

        if (ViewModel is { } tab)
        {
            Editor.SetDiff(tab.FilePath, pendingChange?.DiffLines, pendingChange?.DiffHunks);
        }
    }

    private void OnPendingChangePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProposedFileChangeViewModel.DiffLines) or nameof(ProposedFileChangeViewModel.DiffHunks)
            && ViewModel is { } tab && _pendingChange is { } pendingChange)
        {
            Editor.SetDiff(tab.FilePath, pendingChange.DiffLines, pendingChange.DiffHunks);
        }
    }

    private async void OnGoToDefinitionRequested(object? sender, CodeNavigationRequestedEventArgs e)
    {
        if (ViewModel is not { } tab || Explorer is not { } explorer)
        {
            return;
        }

        await explorer.GoToDefinitionAsync(tab.FilePath, e.Position);

        // Same-file target: no tab switch happens, so nothing else applies it.
        ApplyPendingCaretTarget();
    }

    private async void OnFindReferencesRequested(object? sender, CodeNavigationRequestedEventArgs e)
    {
        if (ViewModel is not { } tab || Explorer is not { } explorer)
        {
            return;
        }

        var locations = await explorer.FindReferencesAsync(tab.FilePath, e.Position);
        if (locations.Count == 0)
        {
            return;
        }

        ReferencesList.ItemsSource = locations
            .Select(location => new ReferenceListItem($"{Path.GetFileName(location.FilePath)}:{location.Position.Line + 1}", location))
            .ToArray();
        FlyoutBase.ShowAttachedFlyout(Editor);
    }

    private void OnReferenceClicked(object sender, ItemClickEventArgs e)
    {
        FlyoutBase.GetAttachedFlyout(Editor)?.Hide();
        if (e.ClickedItem is ReferenceListItem item)
        {
            NavigateTo(item.Location);
        }
    }

    // Same NavigateToLocation path as a reference pick.
    private void OnOutlineItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is LspDocumentSymbol symbol && ViewModel is { } tab)
        {
            NavigateTo(new LspLocation(tab.FilePath, symbol.SelectionRange.Start));
        }
    }

    // specs/editor-intelligence R5.
    private void OnBreadcrumbSegmentClicked(object? sender, LspDocumentSymbol symbol)
    {
        if (ViewModel is { } tab)
        {
            NavigateTo(new LspLocation(tab.FilePath, symbol.SelectionRange.Start));
        }
    }

    private void NavigateTo(LspLocation location)
    {
        Explorer?.NavigateToLocation(location);

        // Same-file target; a cross-file one is applied when the other tab is shown.
        ApplyPendingCaretTarget();
    }

    // specs/unified-workbench T13.
    private void OnOpenInReviewButtonClick(object sender, RoutedEventArgs e) => Explorer?.AssistantPanel.ShowReview();

    private void ApplyPendingCaretTarget()
    {
        if (ViewModel is not null && ReferenceEquals(Explorer?.SelectedTab, ViewModel) && Explorer.TakePendingCaretTarget() is { } position)
        {
            Editor.RevealPosition(position, focus: true);
        }
    }

    // DiffHunkGutterMargin's line convention - ProposedStartLine,
    // clamped to at least 1.
    private void OnScrollToHunkRequested(DiffHunk hunk) =>
        Editor.RevealPosition(new LspPosition(Math.Max(hunk.ProposedStartLine, 1) - 1, 0), focus: false);

    // Go to Symbol only ever jumps within the focused file.
    private void JumpCaretTo(LspPosition position) => Editor.RevealPosition(position, focus: true);

    private void TakeActiveEditor()
    {
        if (s_activeEditorOwner != this)
        {
            s_activeEditorOwner?.ReleaseActiveEditor();
            s_activeEditorOwner = this;
            _activeEditorContext.NavigationRequested += JumpCaretTo;
        }

        _activeEditorContext.SetCaretPosition(Editor.CaretPosition);
        _activeEditorContext.SetActiveDocument(ViewModel?.FilePath, ViewModel?.DocumentSymbols);
    }

    private void ReleaseActiveEditor()
    {
        if (s_activeEditorOwner != this)
        {
            return;
        }

        s_activeEditorOwner = null;
        _activeEditorContext.NavigationRequested -= JumpCaretTo;
        _activeEditorContext.SetCaretPosition(null);
        _activeEditorContext.SetActiveDocument(null, null);
    }

    // F31.6 finding: after the window is deactivated and reactivated, XAML focus is back on the
    // WebView2 but keystrokes can fail to reach the page. Pushing focus back in fixes it.
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated && Editor.HasXamlFocus)
        {
            Editor.FocusEditor();
        }
    }
}
