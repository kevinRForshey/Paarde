using Paarde.Avalonia.Contracts.Services;

using Paarde.Core.Models;

namespace Paarde.Avalonia.Services;

public class ActiveEditorContext : IActiveEditorContext
{
    public event Action<LspPosition?>? Changed;

    public void SetCaretPosition(LspPosition? position) => Changed?.Invoke(position);

    public event Action<string?, LspDocumentSymbol?>? DocumentChanged;

    public void SetActiveDocument(string? filePath, LspDocumentSymbol? documentSymbols) =>
        DocumentChanged?.Invoke(filePath, documentSymbols);

    public event Action<LspPosition>? NavigationRequested;

    public void RequestNavigation(LspPosition position) => NavigationRequested?.Invoke(position);
}
