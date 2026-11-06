using Paarde.Avalonia.Contracts.Services;

using Paarde.Core.Models;

namespace Paarde.Avalonia.Tests.Fakes;

public class FakeActiveEditorContext : IActiveEditorContext
{
    public List<LspPosition?> SetCaretPositionCalls { get; } = [];

    public List<LspPosition> RequestNavigationCalls { get; } = [];

    public event Action<LspPosition?>? Changed;

    public void SetCaretPosition(LspPosition? position)
    {
        SetCaretPositionCalls.Add(position);
        Changed?.Invoke(position);
    }

    public event Action<string?, LspDocumentSymbol?>? DocumentChanged;

    public void SetActiveDocument(string? filePath, LspDocumentSymbol? documentSymbols) =>
        DocumentChanged?.Invoke(filePath, documentSymbols);

    public event Action<LspPosition>? NavigationRequested;

    public void RequestNavigation(LspPosition position)
    {
        RequestNavigationCalls.Add(position);
        NavigationRequested?.Invoke(position);
    }
}
