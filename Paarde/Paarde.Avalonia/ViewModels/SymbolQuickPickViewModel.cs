using System.Collections.ObjectModel;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Strings;
using Paarde.Core.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paarde.Avalonia.ViewModels;

// specs/editor-intelligence R7: a DI singleton (ShellViewModel property, same "singleton handed
// in" shape as QuickOpenViewModel/CommandPaletteViewModel) so Ctrl+Shift+O reaches the same
// instance regardless of which page is currently navigated to. Reads/jumps the focused tab's own
// outline entirely through IActiveEditorContext (DocumentChanged/RequestNavigation) rather than
// depending on the page-scoped ExplorerViewModel, the same reason StatusBarViewModel already
// reaches it only through that context for the caret readout.
public partial class SymbolQuickPickViewModel : ObservableObject
{
    private readonly IActiveEditorContext _activeEditorContext;

    private string? _activeFilePath;
    private IReadOnlyList<LspDocumentSymbol> _flattenedSymbols = [];

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string query = string.Empty;

    [ObservableProperty]
    private LspDocumentSymbol? selectedResult;

    [ObservableProperty]
    private string? emptyStateMessage;

    public ObservableCollection<LspDocumentSymbol> Results { get; } = new();

    public SymbolQuickPickViewModel(IActiveEditorContext activeEditorContext)
    {
        _activeEditorContext = activeEditorContext;
        activeEditorContext.DocumentChanged += OnActiveDocumentChanged;
    }

    private void OnActiveDocumentChanged(string? filePath, LspDocumentSymbol? documentSymbols)
    {
        _activeFilePath = filePath;
        _flattenedSymbols = documentSymbols is null ? [] : Flatten(documentSymbols.Children).ToList();
    }

    [RelayCommand]
    private void Open()
    {
        Query = string.Empty;
        Refilter(Query);
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void ActivateSelected()
    {
        if (SelectedResult is not { } symbol || _activeFilePath is null)
        {
            return;
        }

        _activeEditorContext.RequestNavigation(symbol.SelectionRange.Start);
        IsOpen = false;
    }

    partial void OnQueryChanged(string value) => Refilter(value);

    private void Refilter(string query)
    {
        Results.Clear();
        SelectedResult = null;

        if (_activeFilePath is null)
        {
            EmptyStateMessage = "SymbolQuickPick_NoActiveDocument".Resolve();
            return;
        }

        var matches = string.IsNullOrWhiteSpace(query)
            ? _flattenedSymbols
            : _flattenedSymbols.Where(symbol => symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        var matchList = matches.ToList();
        if (matchList.Count == 0)
        {
            EmptyStateMessage = "SymbolQuickPick_NoMatches".Resolve();
            return;
        }

        EmptyStateMessage = null;
        foreach (var match in matchList)
        {
            Results.Add(match);
        }

        SelectedResult = Results.FirstOrDefault();
    }

    private static IEnumerable<LspDocumentSymbol> Flatten(IReadOnlyList<LspDocumentSymbol> symbols)
    {
        foreach (var symbol in symbols)
        {
            yield return symbol;

            foreach (var child in Flatten(symbol.Children))
            {
                yield return child;
            }
        }
    }
}
