using Paarde.Avalonia.Contracts.Services;

using Paarde.Core.Models;

namespace Paarde.Avalonia.Services;

public class ActiveEditorContext : IActiveEditorContext
{
    public event Action<LspPosition?>? Changed;

    public void SetCaretPosition(LspPosition? position) => Changed?.Invoke(position);
}
