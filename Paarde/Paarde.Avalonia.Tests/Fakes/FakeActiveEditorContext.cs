using Paarde.Avalonia.Contracts.Services;

using Paarde.Core.Models;

namespace Paarde.Avalonia.Tests.Fakes;

public class FakeActiveEditorContext : IActiveEditorContext
{
    public List<LspPosition?> SetCaretPositionCalls { get; } = [];

    public event Action<LspPosition?>? Changed;

    public void SetCaretPosition(LspPosition? position)
    {
        SetCaretPositionCalls.Add(position);
        Changed?.Invoke(position);
    }
}
