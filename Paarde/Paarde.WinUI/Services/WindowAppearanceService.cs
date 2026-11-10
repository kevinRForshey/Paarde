using Paarde.Presentation.Contracts.Services;

namespace Paarde.WinUI.Services;

public class WindowAppearanceService : IWindowAppearanceService
{
    public void ApplyTransparency(bool enabled) => App.MainWindow?.ApplyTransparency(enabled);
}
